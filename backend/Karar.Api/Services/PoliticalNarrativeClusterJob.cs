using Npgsql;

namespace Karar.Api.Services;

// Hourly background job: detects coordinated political narrative campaigns per category.
// Alert fires when: post rate in a 6h window is ≥3× the 30-day median AND
// the median account age of the posting devices is <72h.
// Also auto-throttles the affected category for 4 hours.
public sealed class PoliticalNarrativeClusterJob(
    IConfiguration configuration,
    ILogger<PoliticalNarrativeClusterJob> logger,
    EmailService emailService,
    CategoryThrottleService throttleService)
    : BackgroundService
{
    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan AutoThrottleDuration = TimeSpan.FromHours(4);
    private const double SpikeMultiplier = 3.0;
    private const int YoungAccountMaxHours = 72;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PoliticalNarrativeClusterJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForNarrativeClustersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PoliticalNarrativeClusterJob error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CheckForNarrativeClustersAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Get categories with their recent and median post counts
        await using var categoriesCmd = new NpgsqlCommand(
            """
            WITH recent AS (
                SELECT category_id, COUNT(*) AS recent_count
                FROM posts
                WHERE created_at >= NOW() - INTERVAL '6 hours'
                  AND status != 'deleted'
                GROUP BY category_id
            ),
            medians AS (
                SELECT
                    category_id,
                    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY daily_count) / 4.0 AS median_6h
                FROM (
                    SELECT category_id, DATE_TRUNC('day', created_at) AS day, COUNT(*) AS daily_count
                    FROM posts
                    WHERE created_at >= NOW() - INTERVAL '30 days'
                      AND status != 'deleted'
                    GROUP BY category_id, DATE_TRUNC('day', created_at)
                ) daily
                GROUP BY category_id
            )
            SELECT r.category_id, r.recent_count, m.median_6h, c.name
            FROM recent r
            JOIN medians m ON m.category_id = r.category_id
            JOIN categories c ON c.id = r.category_id
            WHERE m.median_6h >= 1
              AND r.recent_count >= m.median_6h * @spikeMultiplier
            """,
            connection
        );
        categoriesCmd.Parameters.AddWithValue("spikeMultiplier", SpikeMultiplier);

        var flaggedCategories = new List<(int Id, string Name, double RecentCount, double Median)>();
        await using (var reader = await categoriesCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                flaggedCategories.Add((
                    reader.GetInt32(0),
                    reader.GetString(3),
                    Convert.ToDouble(reader.GetValue(1)),
                    Convert.ToDouble(reader.GetValue(2))
                ));
            }
        }

        foreach (var (categoryId, categoryName, recentCount, median) in flaggedCategories)
        {
            // Check median account age of posting devices in this category
            await using var ageCmd = new NpgsqlCommand(
                """
                SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM (NOW() - d.created_at)) / 3600.0
                )
                FROM posts p
                JOIN devices d ON d.id = p.device_id
                WHERE p.created_at >= NOW() - INTERVAL '6 hours'
                  AND p.status != 'deleted'
                  AND p.category_id = @categoryId
                """,
                connection
            );
            ageCmd.Parameters.AddWithValue("categoryId", categoryId);

            var ageResult = await ageCmd.ExecuteScalarAsync(ct);
            if (ageResult is null or DBNull) continue;
            var medianAccountAgeHours = Convert.ToDouble(ageResult);

            if (medianAccountAgeHours > YoungAccountMaxHours) continue;

            logger.LogWarning(
                "POLITICAL_NARRATIVE_ALERT: Kategori={Category}, {RecentCount} gönderi/6h " +
                "(medyan {Median:.1f}), medyan hesap yaşı {Age:.0f}h",
                categoryName, recentCount, median, medianAccountAgeHours
            );

            var alreadyThrottled = await throttleService.IsThrottledAsync(categoryId);
            if (!alreadyThrottled)
            {
                var reason = $"Koordineli anlatı tespiti: {recentCount:F0} gönderi/6h (normalin {recentCount / median:F1}×'i), " +
                             $"medyan hesap yaşı {medianAccountAgeHours:F0}h";
                await throttleService.SetThrottledAsync(categoryId, AutoThrottleDuration, reason);
            }

            await NotifyAdminAsync(categoryName, recentCount, median, medianAccountAgeHours, ct);
        }
    }

    private async Task NotifyAdminAsync(
        string categoryName, double recentCount, double median, double accountAgeHours,
        CancellationToken ct)
    {
        try
        {
            var subject = "[Karar] Koordineli Anlatı Uyarısı";
            var body = $"""
                Koordineli paylaşım kampanyası tespit edildi.

                Kategori: {categoryName}
                Son 6 saat içinde {recentCount:F0} gönderi paylaşıldı.
                30 günlük 6 saatlik medyan: {median:F1}
                Spike çarpanı: {recentCount / median:F1}x
                Paylaşan cihazların medyan hesap yaşı: {accountAgeHours:F0} saat

                Kategori otomatik olarak 4 saat kısıtlamaya alındı.
                Lütfen admin panelinden moderasyon kuyruğunu kontrol edin.
                """;

            await emailService.SendAdminAlertAsync(subject, body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin alert email gönderilemedi");
        }
    }
}
