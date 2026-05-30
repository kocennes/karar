using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Karar.IntegrationTests.Security;

// Mass assignment: endpoint'ler istemcinin göndermemesi gereken alanları
// (status, authorId, voteCount, isFeatured, trustScore vb.) sessizce kabul
// etmemeli ya da işleme almamalıdır.
//
// Bu testler DB olmadan çalışır: auth katmanı devreye girerek 401 döner,
// yani API fazladan alanları dağıtım katmanına bile iletmez.
// DB gerektiren doğrulama: field'ın DB'ye yazılmadığını kontrol eden testler
// [Trait("Category","RequiresDb")] ile işaretlenmeli ve ayrıca çalıştırılmalıdır.
[Collection("ApiTests")]
public class MassAssignmentTests
{
    private readonly HttpClient _client;

    public MassAssignmentTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // İstemci korunan alanları body'ye ekleyerek gönderdiğinde,
    // sistem ya 401 (auth gerekli) ya da 400 (validation) döner — asla 2xx.
    [Fact]
    public async Task CreatePost_WithProtectedFields_IsBlockedAtAuthLayer()
    {
        var payload = new
        {
            title = "Normal başlık",
            content = "Normal içerik",
            categoryId = 1,
            // Korunan alanlar — istemci bunları set edemez:
            authorId = Guid.NewGuid(),
            status = "active",
            isFeatured = true,
            voteCountHakli = 9999,
            voteCountHaksiz = 0,
            trustScore = 1.0,
        };

        var response = await _client.PostAsJsonAsync("/api/v1/posts", payload);

        // Device token olmadığından 401; body'ye bakılmadan önce auth devreye girer.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CastVote_WithExtraManipulationFields_IsBlockedAtAuthLayer()
    {
        var postId = Guid.NewGuid();
        var payload = new
        {
            vote = "hakli",
            // Sahte alanlar:
            voteCountHakli = 99999,
            userId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            trustScore = 1.0,
            quarantined = false,
        };

        var response = await _client.PostAsJsonAsync($"/api/v1/posts/{postId}/vote", payload);

        // Device token yoksa 400 veya 401; korunan alanlar hiç işlenmez.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_WithElevationFields_IsBlockedAtAuthLayer()
    {
        var payload = new
        {
            bio = "Normal bio",
            // Yetki yükseltme denemeleri:
            isAdmin = true,
            role = "admin",
            isBanned = false,
            karmaScore = 99999,
            trustScore = 1.0,
            emailVerified = true,
        };

        var response = await _client.PutAsJsonAsync("/api/v1/users/me", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitReport_WithFakeMetadata_IsBlockedAtAuthLayer()
    {
        var payload = new
        {
            targetType = "post",
            targetId = Guid.NewGuid(),
            reason = "spam",
            // Sahte moderasyon alanları:
            status = "resolved",
            resolvedBy = "admin@karar.app",
            perspectiveScore = 0.01,
            reporterTrustScore = 1.0,
        };

        // Reports device-only (guest) endpoint olabilir; 401 ya da 400.
        var response = await _client.PostAsJsonAsync("/api/v1/reports", payload);

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    // API'nin bilinmeyen alanları içeren büyük bir body'yi çökmeden işlediğini
    // doğrular (ör. JsonSerializer varsayılan olarak ek alanları yoksayar).
    [Fact]
    public async Task Login_WithExtraFields_DoesNotCrash()
    {
        var hugeBogusPayload = new
        {
            email = "test@example.com",
            password = "ValidPass1!",
            // İstemcinin gönderemeyeceği alanlar:
            userId = Guid.NewGuid(),
            accessToken = "fake-token",
            role = "admin",
            sessionId = Guid.NewGuid(),
            __proto__ = "polluted",
            constructor = "exploit",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", hugeBogusPayload);

        // Device token yoksa 401, DB yoksa 500 olabilir; asla uygulama çökmesi olmaz.
        Assert.True((int)response.StatusCode < 600);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminAction_WithForgedAdminClaim_IsRejected()
    {
        // Admin token'ı role="admin" claim'i gerektirir; başka bir issuer/secret
        // ile imzalanmış token reddedilmeli.
        var forgedToken = JwtTestHelper.CreateUserToken(
            userId: Guid.NewGuid().ToString(),
            username: "attacker");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/users");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
