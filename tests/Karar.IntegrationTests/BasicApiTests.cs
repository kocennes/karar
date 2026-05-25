using System.Net;
using System.Net.Http.Json;

namespace Karar.IntegrationTests;

// Bu testler DB gerektirmez — middleware, routing ve header davranışlarını doğrular.
// DB gerektiren testler için [Trait("Category", "RequiresDb")] kullan ve
// Docker stack çalışırken çalıştır: docker-compose up -d
[Collection("ApiTests")]
public class SecurityHeaderTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SecurityHeaderTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AllResponses_HaveXContentTypeOptions()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.True(
            response.Headers.TryGetValues("X-Content-Type-Options", out var values) &&
            values.Contains("nosniff"));
    }

    [Fact]
    public async Task AllResponses_HaveXFrameOptions()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.True(
            response.Headers.TryGetValues("X-Frame-Options", out var values) &&
            values.Contains("DENY"));
    }

    [Fact]
    public async Task AllResponses_HaveReferrerPolicy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.True(
            response.Headers.TryGetValues("Referrer-Policy", out var values) &&
            values.Contains("no-referrer"));
    }

    [Fact]
    public async Task AllResponses_HavePermissionsPolicy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.True(
            response.Headers.TryGetValues("Permissions-Policy", out _));
    }

    [Fact]
    public async Task AllResponses_HaveContentSecurityPolicy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.True(
            response.Headers.TryGetValues("Content-Security-Policy", out _));
    }
}

[Collection("ApiTests")]
public class HealthCheckTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthCheckTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsOk_WithoutDatabase()
    {
        // /health/live sadece "self" check yapar — DB/Redis gerektirmez
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/nonexistent-endpoint-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Version_ReturnsForceUpdateContract()
    {
        var response = await _client.GetAsync("/api/v1/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal("1.0.0", body["minimumVersion"]);
        Assert.StartsWith("https://", body["androidStoreUrl"]);
        Assert.StartsWith("https://", body["iosStoreUrl"]);
    }

    [Fact]
    public async Task AdminEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/admin/moderation/queue");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

[Collection("ApiTests")]
public class CorsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CorsPreflightFromAllowedOrigin_ReturnsOk_InDevelopment()
    {
        // Development modunda AllowAnyOrigin aktif
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/categories");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await _client.SendAsync(request);
        // OPTIONS ya 204 ya da 200 döner (CORS middleware'e göre değişir)
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.OK ||
            response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

[Collection("ApiTests")]
public class AuthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // Register endpoint device token kontrolü yaparak başlar:
    // token yoksa 401, token varsa validation ve sonra DB.
    [Fact]
    public async Task Register_WithoutDeviceToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "testuser",
            email = "test@example.com",
            password = "ValidPass1!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithoutDeviceToken_ReturnsErrorStatus()
    {
        // Device token yoksa, login endpoint ya 401 ya da DB erişim hatası verir (DB yok)
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "test@example.com",
            password = "ValidPass1!",
        });
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            (int)response.StatusCode >= 400);
    }

    [Fact]
    public async Task VoteEndpoint_WithoutDeviceToken_ReturnsClientError()
    {
        // Device token yoksa vote → 400 veya 401
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/posts/{Guid.NewGuid()}/vote",
            new { vote = "hakli" });
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeviceRegister_WithMissingBody_ReturnsClientError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/devices/register", new { });
        Assert.True((int)response.StatusCode >= 400);
    }

    [Fact]
    public async Task AdminLogin_WithBadCredentials_ReturnsClientError()
    {
        // Admin login: wrong credentials → 401; DB yok → 500
        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/login", new
        {
            email = "wrong@admin.com",
            password = "wrongpassword",
            totpCode = "000000",
        });
        Assert.True((int)response.StatusCode >= 400);
    }

    [Fact]
    public async Task DeviceNonce_ReturnsOkWithNonce()
    {
        var response = await _client.GetAsync("/api/v1/devices/nonce");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("nonce", body);
    }

    [Fact]
    public async Task NotificationPreferences_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/users/me/notification-preferences");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
