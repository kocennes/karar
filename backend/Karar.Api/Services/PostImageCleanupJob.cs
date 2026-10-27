using System.Text.RegularExpressions;
using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Deletes stored post images that must no longer be publicly reachable, then clears posts.image_url:
/// - posts deleted by their owner or by an admin (status = 'deleted'),
/// - posts that are live again but whose image SafeSearch rejected (that image can never be shown).
/// Posts still awaiting review (under_review / auto_hidden) keep their image for the moderators.
/// The job is driven by DB state, so a failed storage call is simply retried on the next run.
/// </summary>
public sealed partial class PostImageCleanupJob(
    Db db,
    IStorageService storage,
    ILogger<PostImageCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PostImageCleanupJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PostImageCleanupJob error.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        if (!storage.IsEnabled) return;

        await using var connection = await db.OpenConnectionAsync();

        var candidates = new List<(Guid PostId, string ImageUrl)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id, image_url
            FROM posts
            WHERE image_url IS NOT NULL
              AND (status = 'deleted' OR (status = 'active' AND image_moderation_status = 'rejected'))
            ORDER BY updated_at ASC
            LIMIT @batchSize
            """,
            connection))
        {
            select.Parameters.AddWithValue("batchSize", BatchSize);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                candidates.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        if (candidates.Count == 0) return;

        var purged = await PurgeAsync(
            candidates,
            storage,
            (postId, imageUrl) => ClearImageUrlAsync(connection, postId, imageUrl, ct),
            logger,
            ct);
        logger.LogInformation("PostImageCleanupJob purged {Purged}/{Total} post images.", purged, candidates.Count);
    }

    /// <summary>
    /// Deletes each candidate's object and clears its reference. A failed delete keeps image_url so the
    /// next run retries; a URL that is not one of our uploads only has its reference cleared.
    /// </summary>
    public static async Task<int> PurgeAsync(
        IReadOnlyList<(Guid PostId, string ImageUrl)> candidates,
        IStorageService storage,
        Func<Guid, string, Task> clearImageUrl,
        ILogger logger,
        CancellationToken ct = default)
    {
        var purged = 0;
        foreach (var (postId, imageUrl) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (TryGetObjectName(imageUrl, out var objectName))
            {
                if (!await storage.DeleteAsync(objectName, ct))
                {
                    logger.LogWarning("Image of post {PostId} could not be deleted from storage; will retry.", postId);
                    continue;
                }
            }
            else
            {
                logger.LogWarning("Post {PostId} image_url is not a recognised upload; clearing the reference only.", postId);
            }

            await clearImageUrl(postId, imageUrl);
            purged++;
        }

        return purged;
    }

    /// <summary>
    /// Uploads are always stored as "posts/{guid}.jpg" (POST /api/v1/posts). The public URL ends with that
    /// object name for GCS (storage.googleapis.com or the CDN base) and for Supabase
    /// (/storage/v1/object/public/{bucket}/...). Anything else is refused, and the object name is rebuilt
    /// from the GUID alone, so a stored value can never point the delete at another object.
    /// </summary>
    public static bool TryGetObjectName(string? imageUrl, out string objectName)
    {
        objectName = string.Empty;
        if (string.IsNullOrWhiteSpace(imageUrl)
            || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var match = PostImagePath().Match(uri.AbsolutePath);
        if (!match.Success) return false;

        objectName = $"posts/{match.Groups[1].Value}.jpg";
        return true;
    }

    [GeneratedRegex("/posts/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\\.jpg$")]
    private static partial Regex PostImagePath();

    private static async Task ClearImageUrlAsync(NpgsqlConnection connection, Guid postId, string imageUrl, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE posts SET image_url = NULL WHERE id = @postId AND image_url = @imageUrl",
            connection);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("imageUrl", imageUrl);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
