using System.Text.Json;
using Karar.Api.Data;
using Karar.Api.Observability;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Background service that runs SafeSearch on newly uploaded post images.
/// Polls every 30 seconds for posts with image_moderation_status = 'pending'.
/// Fail closed: an image is only ever shown after an explicit SafeSearch verdict; when Vision gives
/// no verdict the image stays 'pending' (hidden on every public endpoint) and is retried with backoff.
/// </summary>
public sealed class ImageModerationWorker(
    Db db,
    SafeSearchService safeSearch,
    ActivityTracker activityTracker,
    ILogger<ImageModerationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    /// <summary>After this many verdict-less attempts an admin alert is raised (the image stays hidden).</summary>
    public const int AlertAfterAttempts = 5;

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// Per-image decision. <paramref name="result"/> is null when SafeSearch returned no verdict
    /// (disabled, non-2xx such as quota/key/billing errors, timeout, or a response without
    /// safeSearchAnnotation). That must never approve the image.
    /// </summary>
    public static ImageModerationDecision Decide(SafeSearchResult? result, int previousAttempts)
    {
        if (result is null)
        {
            var attempts = previousAttempts + 1;
            return ImageModerationDecision.Retry(RetryDelay(attempts), raiseAlert: attempts == AlertAfterAttempts);
        }

        var (postStatus, moderationStatus) = SafeSearchService.DetermineOutcome(result);
        return ImageModerationDecision.Final(postStatus, moderationStatus);
    }

    /// <summary>Exponential backoff: 1, 2, 4, ... minutes, capped at <see cref="MaxRetryDelay"/>.</summary>
    public static TimeSpan RetryDelay(int attempts)
    {
        var minutes = Math.Pow(2, Math.Clamp(attempts - 1, 0, 30));
        return TimeSpan.FromMinutes(Math.Min(minutes, MaxRetryDelay.TotalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ImageModerationWorker started.");
        if (!safeSearch.IsEnabled)
        {
            logger.LogWarning(
                "Vision:ApiKey is not configured: image moderation is disabled, new image uploads are rejected and pending images stay hidden.");
        }

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

        // Pending images only come from uploads (user activity); nobody around means nothing to scan.
        if (activityTracker.IsIdle())
        {
            logger.LogDebug("ImageModerationWorker: API idle, cycle skipped (free tier).");
            return;
        }

        using var activity = KararTelemetry.StartActivity("image_moderation_worker.process_batch");
        activity?.SetTag("db.system", "postgresql");

        await using var connection = await db.OpenConnectionAsync();

        // Oldest due images first, no age cutoff: a burst of new uploads cannot starve older ones, and
        // images that keep failing back off (next_attempt_at) instead of blocking the head of the queue.
        await using var selectCmd = new NpgsqlCommand(
            """
            SELECT id, image_url, image_moderation_attempts
            FROM posts
            WHERE image_moderation_status = 'pending'
              AND image_url IS NOT NULL
              AND (image_moderation_next_attempt_at IS NULL OR image_moderation_next_attempt_at <= NOW())
            ORDER BY created_at ASC
            LIMIT 20
            """,
            connection
        );

        var pending = new List<(Guid Id, string ImageUrl, int Attempts)>();
        await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                pending.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }

        if (pending.Count == 0) return;

        activity?.SetTag("karar.image_count", pending.Count);
        logger.LogInformation("ImageModerationWorker: processing {Count} images.", pending.Count);

        foreach (var (postId, imageUrl, attempts) in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Analyze directly via public URL or GCS URI
            // Cloud Vision supports publicly accessible HTTP/HTTPS URLs.
            var result = await safeSearch.AnalyzeAsync(imageUrl, ct);
            var decision = Decide(result, attempts);
            if (!decision.IsFinal)
            {
                // No verdict: keep the image pending (hidden everywhere) and retry later. Never approve by default.
                await DeferAsync(connection, postId, decision.RetryAfter, ct);
                logger.LogWarning(
                    "SafeSearch returned no verdict for post {PostId} (attempt {Attempt}); image stays hidden, retry in {Delay}.",
                    postId, attempts + 1, decision.RetryAfter);
                if (decision.RaiseAlert)
                {
                    await InsertStalledAlertAsync(connection, postId, attempts + 1, ct);
                    logger.LogError(
                        "Image moderation for post {PostId} stalled after {Attempts} attempts; admin alert raised.",
                        postId, attempts + 1);
                }
                continue;
            }

            await SetModerationStatusAsync(connection, postId, decision.PostStatus, decision.ModerationStatus!, ct);

            if (decision.PostStatus != "active")
            {
                logger.LogWarning(
                    "Post {PostId} image flagged — adult:{Adult} violence:{Violence} racy:{Racy} → {Status}",
                    postId, result!.Adult, result.Violence, result.Racy, decision.PostStatus);
            }
        }
    }

    private static async Task DeferAsync(NpgsqlConnection connection, Guid postId, TimeSpan delay, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE posts
            SET image_moderation_attempts = image_moderation_attempts + 1,
                image_moderation_next_attempt_at = NOW() + (@delaySeconds * INTERVAL '1 second')
            WHERE id = @postId AND image_moderation_status = 'pending'
            """,
            connection
        );
        cmd.Parameters.AddWithValue("delaySeconds", delay.TotalSeconds);
        cmd.Parameters.AddWithValue("postId", postId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertStalledAlertAsync(NpgsqlConnection connection, Guid postId, int attempts, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            post_id = postId,
            attempts,
            detected_at = DateTimeOffset.UtcNow
        });

        // One open alert per hour is enough: a Vision outage stalls every pending image at once and
        // must not bury other admin alerts.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO admin_alerts (type, payload)
            SELECT 'image_moderation_stalled', @payload::jsonb
            WHERE NOT EXISTS (
                SELECT 1 FROM admin_alerts
                WHERE type = 'image_moderation_stalled'
                  AND is_resolved = FALSE
                  AND created_at > NOW() - INTERVAL '1 hour'
            )
            """,
            connection
        );
        cmd.Parameters.AddWithValue("payload", payload);
        await cmd.ExecuteNonQueryAsync(ct);
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

/// <summary>
/// Outcome of one SafeSearch pass. Final: a verdict was obtained (approved/rejected).
/// Not final: no verdict — the image stays 'pending' (hidden) and is retried after <see cref="RetryAfter"/>.
/// </summary>
public sealed record ImageModerationDecision(
    string? PostStatus,
    string? ModerationStatus,
    TimeSpan RetryAfter,
    bool RaiseAlert)
{
    public bool IsFinal => ModerationStatus is not null;

    public static ImageModerationDecision Final(string postStatus, string moderationStatus) =>
        new(postStatus, moderationStatus, TimeSpan.Zero, RaiseAlert: false);

    public static ImageModerationDecision Retry(TimeSpan delay, bool raiseAlert) =>
        new(null, null, delay, raiseAlert);
}
