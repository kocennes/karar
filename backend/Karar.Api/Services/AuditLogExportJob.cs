using System.Text;
using System.Text.Json;
using Google.Cloud.Storage.V1;
using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Daily background job: exports yesterday's admin_actions to GCS as NDJSON.
/// Configure AuditLogs:GcsBucket to enable; job is no-op when bucket is not set.
/// Object path: audit-logs/YYYY/MM/DD/admin-actions-YYYY-MM-DD.ndjson
/// </summary>
public sealed class AuditLogExportJob(
    IConfiguration configuration,
    Db db,
    ILogger<AuditLogExportJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly string? _bucketName =
        configuration["AuditLogs:GcsBucket"];

    private DateTime _lastExportDate = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_bucketName))
        {
            logger.LogInformation(
                "AuditLogExportJob: AuditLogs:GcsBucket yapılandırılmamış, iş devre dışı.");
            return;
        }

        logger.LogInformation("AuditLogExportJob başladı. Hedef bucket: {Bucket}", _bucketName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryExportAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AuditLogExportJob hatası.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task TryExportAsync(CancellationToken ct)
    {
        // Gün içinde sadece bir kez export et (UTC 00:xx aralığında tetiklenir)
        var utcNow = DateTime.UtcNow;
        var targetDate = utcNow.Date.AddDays(-1); // Dün

        if (targetDate <= _lastExportDate)
            return;

        // Saat 00:00 – 01:00 aralığında çalış (job start'ı bekleme süresiyle kayar)
        if (utcNow.Hour != 0)
            return;

        await ExportDayAsync(targetDate, ct);
        _lastExportDate = targetDate;
    }

    internal async Task ExportDayAsync(DateTime date, CancellationToken ct)
    {
        var objectName = $"audit-logs/{date:yyyy/MM/dd}/admin-actions-{date:yyyy-MM-dd}.ndjson";

        logger.LogInformation("AuditLogExport: {Date} için export başlatılıyor → gs://{Bucket}/{Object}",
            date.ToString("yyyy-MM-dd"), _bucketName, objectName);

        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, admin_email, action, target_type, target_id, note, created_at
            FROM admin_actions
            WHERE created_at >= @start AND created_at < @end
            ORDER BY created_at
            """,
            connection);

        command.Parameters.AddWithValue("start", new DateTimeOffset(date, TimeSpan.Zero));
        command.Parameters.AddWithValue("end", new DateTimeOffset(date.AddDays(1), TimeSpan.Zero));

        var rows = new List<AdminActionRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AdminActionRow(
                Id: reader.GetGuid(0),
                AdminEmail: reader.GetString(1),
                Action: reader.GetString(2),
                TargetType: reader.GetString(3),
                TargetId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
                Note: reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(6)
            ));
        }

        if (rows.Count == 0)
        {
            logger.LogInformation("AuditLogExport: {Date} için kayıt bulunamadı, atlanıyor.", date.ToString("yyyy-MM-dd"));
            return;
        }

        var ndjson = BuildNdjson(rows);
        var bytes = Encoding.UTF8.GetBytes(ndjson);

        await using var stream = new MemoryStream(bytes);
        var storageClient = await StorageClient.CreateAsync();
        await storageClient.UploadObjectAsync(
            _bucketName,
            objectName,
            "application/x-ndjson",
            stream,
            cancellationToken: ct);

        logger.LogInformation(
            "AuditLogExport: {Count} satır → gs://{Bucket}/{Object}",
            rows.Count, _bucketName, objectName);
    }

    private static string BuildNdjson(IEnumerable<AdminActionRow> rows)
    {
        var sb = new StringBuilder();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        foreach (var row in rows)
        {
            sb.Append(JsonSerializer.Serialize(row, options));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private record AdminActionRow(
        Guid Id,
        string AdminEmail,
        string Action,
        string TargetType,
        Guid? TargetId,
        string? Note,
        DateTimeOffset CreatedAt);
}
