using Npgsql;
using StackExchange.Redis;

namespace Karar.Api.Services;

public sealed class CommentNotificationBatcher(
    IConfiguration configuration,
    RedisService redis,
    ILogger<CommentNotificationBatcher> logger)
    : BackgroundService
{
    private const string BatchIndexKey = "notif:comment_batches";
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BatchExpiry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    public async Task HandleNewCommentAsync(Guid postId, Guid commenterDeviceId, Guid commentId, Guid? parentCommentId = null)
    {
        if (!await ShouldNotifyAsync(postId, commentId))
        {
            return;
        }

        // Notify parent comment author on reply (before post-owner batch so order is clear)
        if (parentCommentId is not null)
        {
            await NotifyReplyAuthorAsync(postId, parentCommentId.Value, commenterDeviceId, commentId);
        }

        // Notify post owner (batched)
        var ownerDeviceId = await GetPostOwnerDeviceIdAsync(postId);
        if (ownerDeviceId is null || ownerDeviceId == commenterDeviceId)
        {
            return;
        }

        var db = redis.GetDb();
        var key = GetBatchKey(postId, ownerDeviceId.Value);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            var count = await db.SortedSetLengthAsync(key);
            await db.SortedSetAddAsync(key, commentId.ToString("N"), now);
            await db.KeyExpireAsync(key, BatchExpiry);
            await db.SetAddAsync(BatchIndexKey, key);

            if (count == 0)
            {
                await InsertNotificationAsync(
                    ownerDeviceId.Value,
                    "Yeni yorum",
                    "Birisi postuna yorum yaptı.",
                    postId,
                    "comment_on_post");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Comment notification batching failed for post {PostId}", postId);

            await InsertNotificationAsync(
                ownerDeviceId.Value,
                "Yeni yorum",
                "Birisi postuna yorum yaptı.",
                postId,
                "comment_on_post");
        }
    }

    private async Task NotifyReplyAuthorAsync(Guid postId, Guid parentCommentId, Guid commenterDeviceId, Guid replyCommentId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT device_id FROM comments WHERE id = @parentId AND status = 'active'",
            connection);
        command.Parameters.AddWithValue("parentId", parentCommentId);
        var result = await command.ExecuteScalarAsync();

        if (result is not Guid parentAuthorDeviceId || parentAuthorDeviceId == commenterDeviceId)
            return;

        // Use a Redis key to deduplicate reply notifications per reply comment
        var dedupKey = $"notif:reply_sent:{replyCommentId:N}";
        var db = redis.GetDb();
        if (!await db.StringSetAsync(dedupKey, "1", TimeSpan.FromDays(7), When.NotExists))
            return;

        await InsertNotificationAsync(
            parentAuthorDeviceId,
            "Yorum yanıtlandı",
            "Yorumuna birisi yanıt verdi.",
            postId,
            "reply_on_comment");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CommentNotificationBatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushPendingBatchesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CommentNotificationBatcher flush error");
            }

            await Task.Delay(FlushInterval, stoppingToken);
        }
    }

    public async Task FlushPendingBatchesAsync(CancellationToken ct = default)
    {
        var db = redis.GetDb();
        var keys = await db.SetMembersAsync(BatchIndexKey);
        var cutoff = DateTimeOffset.UtcNow.Subtract(BatchWindow).ToUnixTimeSeconds();

        foreach (var value in keys)
        {
            ct.ThrowIfCancellationRequested();

            var key = value.ToString();
            if (!TryParseBatchKey(key, out var postId, out var ownerDeviceId))
            {
                await db.SetRemoveAsync(BatchIndexKey, value);
                continue;
            }

            var count = await db.SortedSetLengthAsync(key);
            if (count == 0)
            {
                await db.SetRemoveAsync(BatchIndexKey, value);
                continue;
            }

            var first = await db.SortedSetRangeByRankWithScoresAsync(key, 0, 0, Order.Ascending);
            if (first.Length == 0 || first[0].Score > cutoff)
            {
                continue;
            }

            if (count > 1)
            {
                await InsertNotificationAsync(
                    ownerDeviceId,
                    "Yeni yorumlar",
                    $"{count - 1} yeni yorum var.",
                    postId);
            }

            await db.KeyDeleteAsync(key);
            await db.SetRemoveAsync(BatchIndexKey, value);
        }
    }

    private async Task<Guid?> GetPostOwnerDeviceIdAsync(Guid postId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT device_id FROM posts WHERE id = @postId AND status = 'active'",
            connection);
        command.Parameters.AddWithValue("postId", postId);
        var result = await command.ExecuteScalarAsync();
        return result is Guid ownerDeviceId ? ownerDeviceId : null;
    }

    private async Task<bool> ShouldNotifyAsync(Guid postId, Guid commentId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.status,
                   char_length(trim(c.content)),
                   u.karma
            FROM comments c
            LEFT JOIN users u ON u.id = c.user_id
            WHERE c.id = @commentId AND c.post_id = @postId
            """,
            connection);
        command.Parameters.AddWithValue("commentId", commentId);
        command.Parameters.AddWithValue("postId", postId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return false;
        }

        var status = reader.GetString(0);
        var contentLength = reader.GetInt32(1);
        var authorKarma = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
        return ShouldNotify(status, contentLength, authorKarma);
    }

    public static bool ShouldNotify(string status, int contentLength, int? authorKarma)
    {
        return status == "active" && contentLength >= 5 && (authorKarma ?? 0) > -10;
    }

    private async Task InsertNotificationAsync(Guid deviceId, string title, string body, Guid postId, string type = "comment_on_post")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO notifications (device_id, type, title, body, post_id)
            VALUES (@deviceId, @type, @title, @body, @postId)
            """,
            connection);
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("postId", postId);
        await command.ExecuteNonQueryAsync();
    }

    private static string GetBatchKey(Guid postId, Guid ownerDeviceId)
    {
        return $"notif:comment_batch:{postId:N}:{ownerDeviceId:N}";
    }

    private static bool TryParseBatchKey(string key, out Guid postId, out Guid ownerDeviceId)
    {
        postId = Guid.Empty;
        ownerDeviceId = Guid.Empty;

        var parts = key.Split(':');
        return parts.Length == 4
            && parts[0] == "notif"
            && parts[1] == "comment_batch"
            && Guid.TryParseExact(parts[2], "N", out postId)
            && Guid.TryParseExact(parts[3], "N", out ownerDeviceId);
    }
}
