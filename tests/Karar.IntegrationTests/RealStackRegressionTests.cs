using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Karar.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Karar.IntegrationTests;

// Regressions for the 500s found the first time the API ran against a real PostgreSQL 16
// (all migrations on an empty database) and a Redis-compatible server. They need a migrated
// database and Redis: point KARAR_TEST_POSTGRES / KARAR_TEST_REDIS at them (see
// CustomWebApplicationFactory) and run with --filter Category=RequiresDb, or the whole suite.
[Collection("ApiTests")]
[Trait("Category", "RequiresDb")]
public class RealStackRegressionTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RealStackRegressionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ModerationTransparency_QueriesExistingColumns()
    {
        // The report is cached for 6h; drop it so the SQL actually runs.
        await _factory.Services.GetRequiredService<RedisService>().GetDb().KeyDeleteAsync("moderation:transparency");

        var response = await _client.GetAsync("/api/v1/moderation/transparency");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Feedback_HasARegisteredRateLimitPolicy()
    {
        var deviceToken = await RegisterDeviceAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/feedback")
        {
            Content = JsonContent.Create(new { type = "bug", subject = "Entegrasyon testi", message = "Gercek yigin uzerinde geri bildirim." }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AiSummary_HasARegisteredRateLimitPolicy()
    {
        var response = await _client.PostAsync($"/api/v1/posts/{Guid.NewGuid()}/ai-summary", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UserPosts_ForAProfileWithPosts_Returns200()
    {
        var deviceToken = await RegisterDeviceAsync();
        var username = $"it_{Guid.NewGuid():N}"[..20];
        var accessToken = await RegisterAndVerifyUserAsync(deviceToken, username);

        using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts"))
        {
            create.Headers.Add("X-Device-Token", deviceToken);
            create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            create.Content = JsonContent.Create(new
            {
                title = "Entegrasyon testi gonderi basligi",
                content = "Entegrasyon testi icin yazilmis, elli karakterden uzun bir gonderi icerigi.",
                categoryId = 1,
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
            });
            var created = await _client.SendAsync(create);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var list = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{username}/posts");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(list);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserPosts_ListsAnAnonymousPostOnlyForItsOwner()
    {
        // Repairing the listing query above made the endpoint reachable; without the visibility filter it
        // listed an anonymous post under its author's username, which attributes it outright.
        var deviceToken = await RegisterDeviceAsync();
        var username = $"it_{Guid.NewGuid():N}"[..20];
        var accessToken = await RegisterAndVerifyUserAsync(deviceToken, username);
        var publicId = await CreatePostAsync(deviceToken, accessToken, "Herkese acik entegrasyon gonderisi", isAnonymous: false);
        var anonymousId = await CreatePostAsync(deviceToken, accessToken, "Anonim entegrasyon testi gonderisi", isAnonymous: true);

        var guestIds = await ListUserPostIdsAsync(username, accessToken: null);
        Assert.Contains(publicId, guestIds);
        Assert.DoesNotContain(anonymousId, guestIds);

        var ownerIds = await ListUserPostIdsAsync(username, accessToken);
        Assert.Contains(publicId, ownerIds);
        Assert.Contains(anonymousId, ownerIds);
    }

    [Fact]
    public async Task MainFeed_FirstTrendingPage_Returns200()
    {
        // Page 1 of the default trending feed runs the diversity pass, whose outer SELECT reads is_owner.
        var deviceToken = await RegisterDeviceAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/posts");
        request.Headers.Add("X-Device-Token", deviceToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DiscoverDwell_PastTenMinutes_IsClampedNotRejected()
    {
        var author = await RegisterDeviceAsync();
        Guid postId;
        using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts"))
        {
            create.Headers.Add("X-Device-Token", author);
            create.Content = JsonContent.Create(new
            {
                title = "Kesfet bekleme suresi testi",
                content = "Kesfet bekleme suresi icin yazilmis, elli karakterden uzun bir gonderi icerigi.",
                categoryId = 1,
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
            });
            var created = await _client.SendAsync(create);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            postId = json.RootElement.GetProperty("id").GetGuid();
        }

        var reader = await RegisterDeviceAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts/discover/events")
        {
            Content = JsonContent.Create(new { postId, eventType = "dwell", dwellSeconds = 700 }),
        };
        request.Headers.Add("X-Device-Token", reader);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/categories/health")]
    [InlineData("/api/v1/admin/analytics/feed-quality/posts")]
    public async Task AdminAnalytics_QueriesRunOnTheRealSchema(string path)
    {
        var adminToken = await AdminLoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/logout")]
    public async Task AuthEndpoints_WithEmptyBody_Return400NotA500(string path)
    {
        var response = await _client.PostAsJsonAsync(path, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithAGenderOutsideTheCheckConstraint_Returns400()
    {
        var deviceToken = await RegisterDeviceAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username = "gender_check_user",
                email = "gender-check@example.test",
                password = "long-enough-password",
                dateOfBirth = "1990-01-01T00:00:00Z",
                gender = "unspecified",
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
                ageConfirmed = true,
            }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_GENDER", await response.Content.ReadAsStringAsync());
    }

    private async Task<string> RegisterDeviceAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/devices/register", new
        {
            fingerprint = $"it-{Guid.NewGuid():N}",
            platform = "web",
            appVersion = "1.0.0",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<string> RegisterAndVerifyUserAsync(string deviceToken, string username)
    {
        var email = $"{username}@example.test";
        using (var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register"))
        {
            register.Headers.Add("X-Device-Token", deviceToken);
            register.Content = JsonContent.Create(new
            {
                username,
                email,
                password = "long-enough-password",
                dateOfBirth = "1990-01-01T00:00:00Z",
                gender = "prefer_not_to_say",
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
                ageConfirmed = true,
            });
            var registered = await _client.SendAsync(register);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        // No e-mail provider in tests: the register handler keeps "<ts>|<hash>|<otp>" in Redis.
        var cached = (string?)await _factory.Services.GetRequiredService<RedisService>().GetDb()
            .StringGetAsync($"otp:verify:{email}");
        Assert.NotNull(cached);
        var otp = cached!.Split('|')[2];

        using var verify = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/verify-email")
        {
            Content = JsonContent.Create(new { email, otp }),
        };
        verify.Headers.Add("X-Device-Token", deviceToken);
        var verified = await _client.SendAsync(verify);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var json = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> CreatePostAsync(string deviceToken, string accessToken, string title, bool isAnonymous)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts");
        create.Headers.Add("X-Device-Token", deviceToken);
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        create.Content = JsonContent.Create(new
        {
            title,
            content = $"{title}: entegrasyon testi icin yazilmis, elli karakterden uzun bir icerik.",
            categoryId = 1,
            isAnonymous,
            acceptedTerms = true,
            acceptedCommunityGuidelines = true,
        });
        var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> ListUserPostIdsAsync(string username, string? accessToken)
    {
        using var list = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/users/{username}/posts");
        if (accessToken is not null)
        {
            list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        var response = await _client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("posts").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid())
            .ToList();
    }

    private async Task<string> AdminLoginAsync()
    {
        // Testing environment without an e-mail provider: credentials alone return a token.
        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/login", new
        {
            email = "admin@test.local",
            password = "test-admin-password",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }
}
