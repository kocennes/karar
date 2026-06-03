using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Karar.IntegrationTests.Security;

// Bu testler DB gerektirmez — kimlik doğrulama katmanının her endpoint'te
// gerçekten çalıştığını doğrular. Gerçek BOLA senaryoları (kullanıcı A'nın
// kullanıcı B'nin kaynağına erişimi) için [Trait("Category","RequiresDb")]
// işaretli testler ayrıca yazılmalıdır.
[Collection("ApiTests")]
public class AccessControlTests
{
    private readonly HttpClient _client;

    public AccessControlTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Guest (JWT yok) → kullanıcıya özel endpoint'ler 401 vermeli ────────

    [Theory]
    [InlineData("GET", "/api/v1/users/me")]
    [InlineData("PUT", "/api/v1/users/me")]
    [InlineData("DELETE", "/api/v1/users/me")]
    [InlineData("GET", "/api/v1/users/me/sessions")]
    [InlineData("GET", "/api/v1/users/me/notification-preferences")]
    [InlineData("PUT", "/api/v1/users/me/notification-preferences")]
    [InlineData("GET", "/api/v1/users/me/policy-status")]
    [InlineData("POST", "/api/v1/users/me/accept-policy")]
    [InlineData("GET", "/api/v1/users/me/posts")]
    [InlineData("GET", "/api/v1/users/me/comments")]
    [InlineData("GET", "/api/v1/users/me/saved")]
    [InlineData("GET", "/api/v1/users/me/karma-history")]
    [InlineData("GET", "/api/v1/users/me/weekly-stats")]
    [InlineData("GET", "/api/v1/users/me/data-export")]
    [InlineData("GET", "/api/v1/users/me/moderation-history")]
    [InlineData("GET", "/api/v1/users/me/blocked")]
    [InlineData("GET", "/api/v1/notifications")]
    [InlineData("GET", "/api/v1/notifications/unread-count")]
    [InlineData("POST", "/api/v1/notifications/clear-read")]
    [InlineData("PUT", "/api/v1/notifications/read-all")]
    [InlineData("POST", "/api/v1/notifications/mute")]
    [InlineData("DELETE", "/api/v1/notifications/mute")]
    [InlineData("POST", "/api/v1/auth/2fa/setup")]
    [InlineData("POST", "/api/v1/auth/2fa/enable")]
    [InlineData("POST", "/api/v1/auth/2fa/disable")]
    [InlineData("POST", "/api/v1/auth/2fa/backup-codes")]
    [InlineData("GET", "/api/v1/auth/2fa/backup-codes/count")]
    [InlineData("PUT", "/api/v1/users/me/password")]
    [InlineData("POST", "/api/v1/auth/change-email/request")]
    [InlineData("POST", "/api/v1/auth/change-email/confirm")]
    public async Task UserEndpoint_WithoutJwt_Returns401(string method, string path)
    {
        var request = BuildRequest(method, path);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Guest → admin endpoint'ler 401 vermeli ───────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/admin/moderation/queue")]
    [InlineData("GET", "/api/v1/admin/users")]
    [InlineData("GET", "/api/v1/admin/posts")]
    [InlineData("GET", "/api/v1/admin/comments")]
    [InlineData("GET", "/api/v1/admin/reports")]
    [InlineData("GET", "/api/v1/admin/devices")]
    [InlineData("GET", "/api/v1/admin/devices/suspicious")]
    [InlineData("GET", "/api/v1/admin/actions")]
    [InlineData("GET", "/api/v1/admin/appeals")]
    [InlineData("GET", "/api/v1/admin/automod/rules")]
    [InlineData("GET", "/api/v1/admin/analytics/overview")]
    [InlineData("GET", "/api/v1/admin/analytics/velocity")]
    [InlineData("GET", "/api/v1/admin/analytics/trends")]
    [InlineData("GET", "/api/v1/admin/analytics/categories")]
    [InlineData("GET", "/api/v1/admin/analytics/moderation")]
    [InlineData("GET", "/api/v1/admin/analytics/moderators")]
    [InlineData("GET", "/api/v1/admin/analytics/retention")]
    [InlineData("GET", "/api/v1/admin/analytics/notifications")]
    [InlineData("GET", "/api/v1/admin/analytics/cache")]
    [InlineData("GET", "/api/v1/admin/categories/health")]
    [InlineData("GET", "/api/v1/admin/analytics/activity")]
    [InlineData("GET", "/api/v1/admin/analytics/funnels")]
    [InlineData("GET", "/api/v1/admin/analytics/reports/timeseries")]
    [InlineData("GET", "/api/v1/admin/analytics/export")]
    [InlineData("POST", "/api/v1/admin/analytics/scheduled-reports")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/posts")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/timeseries")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/export")]
    [InlineData("GET", "/api/v1/admin/analytics/growth")]
    [InlineData("GET", "/api/v1/admin/analytics/north-star")]
    public async Task AdminEndpoint_WithoutAnyToken_Returns401(string method, string path)
    {
        var request = BuildRequest(method, path);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Sıradan kullanıcı JWT'si → admin endpoint'lere erişim engellenmeli ──
    // Admin token'ı role="admin" claim'i gerektirir; kullanıcı JWT'si bu
    // claim'i taşımaz, dolayısıyla 401 alınmalıdır.

    [Theory]
    [InlineData("GET", "/api/v1/admin/moderation/queue")]
    [InlineData("GET", "/api/v1/admin/users")]
    [InlineData("GET", "/api/v1/admin/posts")]
    [InlineData("GET", "/api/v1/admin/reports")]
    [InlineData("GET", "/api/v1/admin/analytics/overview")]
    [InlineData("GET", "/api/v1/admin/analytics/notifications")]
    [InlineData("GET", "/api/v1/admin/analytics/velocity")]
    [InlineData("GET", "/api/v1/admin/devices")]
    [InlineData("GET", "/api/v1/admin/appeals")]
    [InlineData("GET", "/api/v1/admin/actions")]
    [InlineData("GET", "/api/v1/admin/automod/rules")]
    [InlineData("GET", "/api/v1/admin/analytics/activity")]
    [InlineData("GET", "/api/v1/admin/analytics/funnels")]
    [InlineData("GET", "/api/v1/admin/analytics/reports/timeseries")]
    [InlineData("GET", "/api/v1/admin/analytics/export")]
    [InlineData("POST", "/api/v1/admin/analytics/scheduled-reports")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/posts")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/timeseries")]
    [InlineData("GET", "/api/v1/admin/analytics/feed-quality/export")]
    [InlineData("GET", "/api/v1/admin/analytics/growth")]
    [InlineData("GET", "/api/v1/admin/analytics/north-star")]
    public async Task AdminEndpoint_WithUserJwt_Returns401(string method, string path)
    {
        var userToken = JwtTestHelper.CreateUserToken(
            userId: Guid.NewGuid().ToString(),
            username: "attacker");

        var request = BuildRequest(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Tekil kaynak endpoint'leri: UUID bile bilinse erişim engellenmeli ───

    [Theory]
    [InlineData("PUT", "/api/v1/notifications/{id}/read")]
    [InlineData("POST", "/api/v1/notifications/{id}/dismiss")]
    [InlineData("DELETE", "/api/v1/users/me/sessions/{id}")]
    public async Task SingleResourceEndpoint_WithoutJwt_Returns401(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", Guid.NewGuid().ToString());
        var request = BuildRequest(method, path);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Admin single-resource endpoints with GUID path segments — must reject user JWT.
    [Theory]
    [InlineData("GET", "/api/v1/admin/users/{id}")]
    [InlineData("GET", "/api/v1/admin/devices/{id}")]
    [InlineData("POST", "/api/v1/admin/users/{id}/ban")]
    [InlineData("POST", "/api/v1/admin/users/{id}/unban")]
    [InlineData("POST", "/api/v1/admin/users/{id}/warn")]
    [InlineData("POST", "/api/v1/admin/users/{id}/strike")]
    [InlineData("GET", "/api/v1/admin/users/{id}/strikes")]
    [InlineData("GET", "/api/v1/admin/users/{id}/data-export")]
    [InlineData("POST", "/api/v1/admin/devices/{id}/ban")]
    [InlineData("POST", "/api/v1/admin/devices/{id}/unban")]
    [InlineData("POST", "/api/v1/admin/appeals/{id}/decide")]
    public async Task AdminSingleResourceEndpoint_WithUserJwt_Returns401(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", Guid.NewGuid().ToString());
        var userToken = JwtTestHelper.CreateUserToken(Guid.NewGuid().ToString(), "attacker");
        var request = BuildRequest(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Admin endpoints with integer path segments — must reject user JWT.
    // Route constraint {id:int} means GUID paths return 404 from routing, not 401.
    [Theory]
    [InlineData("GET", "/api/v1/admin/categories/1/throttle")]
    [InlineData("POST", "/api/v1/admin/categories/1/throttle")]
    [InlineData("DELETE", "/api/v1/admin/categories/1/throttle")]
    public async Task AdminIntIdEndpoint_WithUserJwt_Returns401(string method, string path)
    {
        var userToken = JwtTestHelper.CreateUserToken(Guid.NewGuid().ToString(), "attacker");
        var request = BuildRequest(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is not "GET" and not "DELETE")
            request.Content = JsonContent.Create(new { });
        return request;
    }
}
