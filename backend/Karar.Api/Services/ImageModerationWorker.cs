using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Background service that runs SafeSearch on newly uploaded post images.
/// Polls every 30 seconds for posts with image_moderation_status = 'pending'.
/// </summary>
public sealed class ImageModerationWorker(
    Db db,
    SafeSearchService safeSearch,
    ILogger<ImageModerationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ImageModerationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ImageModerationWorker error.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        if (!safeSearch.IsEnabled) return;

        await using var connection = await db.OpenConnectionAsync();

        // Only check images uploaded in the last 6 hours (avoid re-checking old stuck rows)
        await using var selectCmd = new NpgsqlCommand(
            """
            SELECT id, image_url
            FROM posts
            WHERE image_moderation_status = 'pending'
              AND created_at > NOW() - INTERVAL '6 hours'
            ORDER BY created_at DESC
            LIMIT 20
            """,
            connection
        );

        var pending = new List<(Guid Id, string ImageUrl)>();
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                pending.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        if (pending.Count == 0) return;
        logger.LogInformation("ImageModerationWorker: processing {Count} images.", pending.Count);

        foreach (var (postId, imageUrl) in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Analyze directly via public URL or GCS URI
            // Cloud Vision supports publicly accessible HTTP/HTTPS URLs.
            var result = await safeSearch.AnalyzeAsync(imageUrl, ct);
            if (result is null)
            {
                // API unavailable — mark as skipped so it's not retried indefinitely
                await SetModerationStatusAsync(connection, postId, null, "approved", ct);
                logger.LogWarning("SafeSearch unavailable for post {PostId}; approved by default.", postId);
                continue;
            }

            var (postStatus, modStatus) = SafeSearchService.DetermineOutcome(result);
            await SetModerationStatusAsync(connection, postId, postStatus, modStatus, ct);

            if (postStatus != "active")
            {
                logger.LogWarning(
                    "Post {PostId} image flagged — adult:{Adult} violence:{Violence} racy:{Racy} → {Status}",
                    postId, result.Adult, result.Violence, result.Racy, postStatus);
            }
        }
    }

    private static async Task SetModerationStatusAsync(
        NpgsqlConnection connection,
        Guid postId,
        string? postStatus,
        string imageModerationStatus,
        CancellationToken ct)
    {
        // Only update post status when it's being degraded (don't re-activate a manually reviewed post)
        var statusSql = postStatus is not null && postStatus != "active"
            ? ", status = @postStatus, moderation_reason = 'Image flagged by SafeSearch.'"
            : string.Empty;

        await using var cmd = new NpgsqlCommand(
            $"""
            UPDATE posts
            SET image_moderation_status = @imageModerationStatus
                {statusSql}
            WHERE id = @postId AND image_moderation_status = 'pending'
            """,
            connection
        );
        cmd.Parameters.AddWithValue("imageModerationStatus", imageModerationStatus);
        cmd.Parameters.AddWithValue("postId", postId);
        if (postStatus is not null && postStatus != "active")
            cmd.Parameters.AddWithValue("postStatus", postStatus);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
