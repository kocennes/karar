namespace Karar.IntegrationTests.Security;

// BOLA/IDOR security contract tests for routes where a real cross-user attack
// needs seeded DB rows. These run in the integration suite but avoid depending
// on local DB state by asserting the production endpoint SQL scopes mutations
// and reads to the caller's JWT user_id or resolved device_id.
public sealed class BolaIdorContractTests
{
    private static readonly string ProgramText = File.ReadAllText(FindRepoFile("backend/Karar.Api/Program.cs"));

    [Theory]
    [InlineData(
        "app.MapDelete(\"/api/v1/posts/{id:guid}\"",
        "// Records a view impression",
        "AND (device_id = @deviceId OR user_id = @userId)",
        "UPDATE posts")]
    [InlineData(
        "app.MapPut(\"/api/v1/posts/{id:guid}\"",
        "app.MapPost(\"/api/v1/posts/{id:guid}/save\"",
        "AND (device_id = @deviceId OR user_id = @userId)",
        "UPDATE posts")]
    [InlineData(
        "app.MapDelete(\"/api/v1/comments/{id:guid}\"",
        "app.MapPut(\"/api/v1/comments/{id:guid}\"",
        "AND (device_id = @deviceId OR user_id = @userId)",
        "UPDATE comments")]
    [InlineData(
        "app.MapPut(\"/api/v1/comments/{id:guid}\"",
        "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"",
        "AND (device_id = @deviceId OR user_id = @userId)",
        "UPDATE comments")]
    public void OwnerScopedWriteEndpoints_ConstrainMutationToCallerIdentity(
        string start,
        string end,
        string ownershipPredicate,
        string mutationStatement)
    {
        var endpoint = Slice(ProgramText, start, end);

        Assert.Contains("GetOptionalUserId(httpRequest, jwtService)", endpoint);
        Assert.Contains("requestDevice.TryGetDeviceIdAsync(httpRequest)", endpoint);
        Assert.Contains("return Unauthorized()", endpoint);
        Assert.Contains(mutationStatement, endpoint);
        Assert.Contains(ownershipPredicate, endpoint);
        Assert.Contains("AddWithValue(\"deviceId\"", endpoint);
        Assert.Contains("AddWithValue(\"userId\"", endpoint);
    }

    [Fact]
    public void PostStatsEndpoint_ReturnsStatsOnlyForPostOwner()
    {
        var endpoint = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/events\"");

        Assert.Contains("return Unauthorized()", endpoint);
        Assert.Contains("SELECT 1 FROM posts WHERE id = @id AND (device_id = @deviceId OR user_id = @userId)", endpoint);
        Assert.Contains("return Forbid(\"NOT_OWNER\"", endpoint);
    }

    [Fact]
    public void SessionDeleteEndpoint_ScopesRevocationToJwtUser()
    {
        var endpoint = Slice(
            ProgramText,
            "app.MapDelete(\"/api/v1/users/me/sessions/{sessionId:guid}\"",
            "app.MapDelete(\"/api/v1/users/me\"");

        Assert.Contains("GetJwtPrincipal(httpRequest, jwtService)", endpoint);
        Assert.Contains("var userId = GetUserId(principal)", endpoint);
        Assert.Contains("UPDATE refresh_tokens SET revoked_at = NOW() WHERE id = @id AND user_id = @userId", endpoint);
    }

    [Fact]
    public void NotificationEndpoints_AreDeviceScopedAndHideRemovedPostDeepLinks()
    {
        var listEndpoint = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/notifications\"",
            "app.MapPut(\"/api/v1/notifications/read-all\"");
        var readEndpoint = Slice(
            ProgramText,
            "app.MapPut(\"/api/v1/notifications/{id:guid}/read\"",
            "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"");
        var dismissEndpoint = Slice(
            ProgramText,
            "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"",
            "app.MapPost(\"/api/v1/notifications/clear-read\"");

        Assert.Contains("WHERE n.device_id = @deviceId", listEndpoint);
        Assert.Contains("n.dismissed_at IS NULL", listEndpoint);
        Assert.Contains("(n.post_id IS NULL OR p.status = 'active')", listEndpoint);

        Assert.Contains("WHERE id = @id AND device_id = @deviceId", readEndpoint);
        Assert.Contains("RETURNING id, device_id", readEndpoint);

        Assert.Contains("WHERE id = @id AND device_id = @deviceId", dismissEndpoint);
        Assert.Contains("RETURNING id, device_id", dismissEndpoint);
    }

    [Fact]
    public void SavedPostEndpoints_ScopeRowsToJwtUser()
    {
        var saveEndpoint = Slice(
            ProgramText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/save\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/save\"");
        var unsaveEndpoint = Slice(
            ProgramText,
            "app.MapDelete(\"/api/v1/posts/{id:guid}/save\"",
            "// \"İlgilenmiyorum\"");

        Assert.Contains("var userId = GetUserId(principal)", saveEndpoint);
        Assert.Contains("INSERT INTO saved_posts (user_id, post_id)", saveEndpoint);
        Assert.Contains("SELECT @userId, @postId", saveEndpoint);
        Assert.Contains("WHERE EXISTS (SELECT 1 FROM posts WHERE id = @postId AND status = 'active')", saveEndpoint);

        Assert.Contains("var userId = GetUserId(principal)", unsaveEndpoint);
        Assert.Contains("DELETE FROM saved_posts WHERE user_id = @userId AND post_id = @postId", unsaveEndpoint);
    }

    [Fact]
    public void MyDataExportEndpoint_DerivesSubjectFromJwtAndNotRouteInput()
    {
        var endpoint = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/users/me/data-export\"",
            "app.MapGet(\"/api/v1/users/me/weekly-stats\"");

        Assert.Contains("GetJwtPrincipal(httpRequest, jwtService)", endpoint);
        Assert.Contains("var userId = GetUserId(principal)", endpoint);
        Assert.Contains("WHERE id = @userId", endpoint);
        Assert.DoesNotContain("{id:guid}", endpoint);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find start marker: {start}");
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find end marker after {start}: {end}");
        return text[startIndex..endIndex];
    }
}
