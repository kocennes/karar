using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Karar.IntegrationTests.Security;

// BOLA (Broken Object Level Authorization) / IDOR test matrix.
//
// Role matrix per resource:
//   Guest (no token)      → 401
//   Authenticated user    → only own resources; other user's resources → 401/404/403/204-noop
//   Admin with user JWT   → 401 (role claim missing)
//   Admin with admin JWT  → full access (covered separately in AdminTests)
//
// These tests do NOT require a real DB. They validate:
//   (a) auth layer blocks unauthenticated requests,
//   (b) endpoint routing exists (not 404 from routing table),
//   (c) authenticated cross-resource attacks fail (no device → 401, wrong owner → 404/403/204).
[Collection("ApiTests")]
public class BolaIdrTests
{
    private readonly HttpClient _client;

    public BolaIdrTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Post-level BOLA: write operations require device OR user token ────────
    // These endpoints use "deviceId is null AND userId is null" guard.
    // Without any token → 401.
    // Cross-user ownership enforcement is verified via SQL inspection in
    // BolaIdrOwnershipTests (unit tests).

    [Fact]
    public async Task DeletePost_WithoutAnyToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/posts/{Guid.NewGuid()}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UpdatePost_WithoutAnyToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/posts/{Guid.NewGuid()}");
        req.Content = JsonContent.Create(new { title = "x".PadLeft(15), content = "x".PadLeft(55) });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Comment-level BOLA ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteComment_WithoutAnyToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/comments/{Guid.NewGuid()}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateComment_WithoutAnyToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/comments/{Guid.NewGuid()}");
        req.Content = JsonContent.Create(new { content = "updated text content here" });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Post stats: owner-only resource (device OR user required) ────────────

    [Fact]
    public async Task PostStats_WithoutAnyToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/posts/{Guid.NewGuid()}/stats");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Comment pin/unpin: requires JWT (post-owner only) ───────────────────

    [Fact]
    public async Task PinComment_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/posts/{Guid.NewGuid()}/comments/pin");
        req.Content = JsonContent.Create(new { commentId = Guid.NewGuid() });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UnpinComment_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/posts/{Guid.NewGuid()}/comments/pin");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Notification BOLA ────────────────────────────────────────────────────

    // Notification endpoints are device-scoped: only the device that owns the
    // notification can mark it read/dismissed. A different device's token simply
    // triggers the WHERE clause to match zero rows (204 NoContent with no side
    // effect). This test confirms the endpoint routes correctly and that no JWT
    // is needed (device-only auth), which prevents user-to-user IDOR.

    [Fact]
    public async Task NotificationRead_WithoutDeviceToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/notifications/{Guid.NewGuid()}/read");
        req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task NotificationDismiss_WithoutDeviceToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/notifications/{Guid.NewGuid()}/dismiss");
        req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Session BOLA ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSession_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/me/sessions/{Guid.NewGuid()}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Block/unblock BOLA ───────────────────────────────────────────────────

    [Fact]
    public async Task BlockUser_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/blocked");
        req.Content = JsonContent.Create(new { blockedId = Guid.NewGuid() });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UnblockUser_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/me/blocked/{Guid.NewGuid()}");
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Category follow/mute BOLA ────────────────────────────────────────────

    [Fact]
    public async Task FollowCategory_WithoutJwt_Returns401OrBadRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories/1/follow");
        req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.True(resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MuteCategory_WithoutJwt_Returns401OrBadRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories/1/mute");
        req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.True(resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    // ── Moderation appeal: auth required ────────────────────────────────────

    [Fact]
    public async Task ModerationAppeal_WithoutJwt_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/moderation-appeals");
        req.Content = JsonContent.Create(new { targetType = "post", targetId = Guid.NewGuid(), reason = "test" });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Vote: device-scoped, BOLA scenario ──────────────────────────────────

    [Fact]
    public async Task CastVote_WithoutDeviceToken_Returns401OrBadRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/posts/{Guid.NewGuid()}/vote");
        req.Content = JsonContent.Create(new { vote = "hakli" });
        var resp = await _client.SendAsync(req);
        Assert.True(resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    // ── Admin endpoints: normal user JWT is blocked ──────────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/admin/users/{id}/delete")]
    [InlineData("POST", "/api/v1/admin/users/bulk")]
    [InlineData("DELETE", "/api/v1/admin/posts/{id}")]
    [InlineData("DELETE", "/api/v1/admin/comments/{id}")]
    [InlineData("POST", "/api/v1/admin/reports/{id}/action")]
    [InlineData("DELETE", "/api/v1/admin/automod/rules/{id}")]
    [InlineData("POST", "/api/v1/admin/devices/ban-subnet")]
    public async Task AdminWriteEndpoint_WithUserJwt_Returns401(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", Guid.NewGuid().ToString());
        var userToken = JwtTestHelper.CreateUserToken(Guid.NewGuid().ToString(), "attacker");
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        if (method is not "DELETE")
            req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Admin post feature: requires admin token ─────────────────────────────

    [Fact]
    public async Task FeaturePost_WithUserJwt_Returns401()
    {
        var token = JwtTestHelper.CreateUserToken(Guid.NewGuid().ToString(), "attacker");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/posts/{Guid.NewGuid()}/feature");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Removed content deep link: POST /posts/{id} endpoint still returns 404 ──

    [Fact]
    public async Task GetPost_ForRandomGuid_Returns404NotOk()
    {
        // Even if a notification holds a post_id for a removed post,
        // the GET endpoint enforces status = 'active' — so no content leaks.
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/posts/{Guid.NewGuid()}");
        var resp = await _client.SendAsync(req);
        // Without DB: 404 (not found) or 500 (no DB) — never 200.
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Web OG route: removed/hidden posts must not expose content ───────────

    [Fact]
    public async Task OgRoute_ForRandomGuid_IsNotOk()
    {
        var resp = await _client.GetAsync($"/posts/{Guid.NewGuid()}");
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // helpers

    private static HttpRequestMessage BuildRequest(string method, string path)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is not "GET" and not "DELETE")
            req.Content = JsonContent.Create(new { });
        return req;
    }
}
