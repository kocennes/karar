using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FirebaseAdmin.Auth;
using Karar.Api.Data;
using Karar.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Karar.IntegrationTests;

// P0-02 neutral age gate, end to end against a migrated PostgreSQL (V95 included) and Redis:
// point KARAR_TEST_POSTGRES / KARAR_TEST_REDIS at them (see CustomWebApplicationFactory) and run
// with --filter Category=RequiresDb, or the whole suite. Every test creates its own devices and
// accounts, so the suite can run repeatedly against the same database.
//
// Each flag combination gets its own host. Google ID tokens are faked (FakeGoogleTokenVerifier):
// a real one cannot be minted in a test.

[Trait("Category", "RequiresDb")]
public sealed class AgeGateFlagsOffTests(AgeGateFlagsOffFactory factory) : IClassFixture<AgeGateFlagsOffFactory>
{
    private readonly AgeGateApi _api = new(factory);

    [Fact]
    public async Task Register_UnderAge_WritesNoUserRow_AndLocksTheDevice()
    {
        var device = await _api.RegisterDeviceAsync();
        var username = AgeGateApi.NewUsername();

        var response = await _api.RegisterAsync(device, username, AgeGateApi.UnderAgeBirthDate);

        await AgeGateApi.AssertErrorAsync(response, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.Equal(0L, await _api.ScalarAsync<long>("SELECT COUNT(*) FROM users WHERE username = @v", username));
        Assert.True(await _api.IsDeviceLockedAsync(device));
    }

    [Fact]
    public async Task Register_OnALockedDevice_WithAnAdultDate_Returns403_AndWritesNoUserRow()
    {
        var device = await _api.RegisterDeviceAsync();
        var first = await _api.RegisterAsync(device, AgeGateApi.NewUsername(), AgeGateApi.UnderAgeBirthDate);
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);

        var username = AgeGateApi.NewUsername();
        var retry = await _api.RegisterAsync(device, username, AgeGateApi.AdultBirthDate);

        await AgeGateApi.AssertErrorAsync(retry, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.Equal(0L, await _api.ScalarAsync<long>("SELECT COUNT(*) FROM users WHERE username = @v", username));
    }

    [Fact]
    public async Task Onboarding_SecondCall_DoesNotOverwriteTheBirthDate()
    {
        var device = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(device);
        Assert.True(google.RequiresOnboarding);

        var first = await _api.OnboardingAsync(google.AccessToken, device, "1990-01-01");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await _api.OnboardingAsync(google.AccessToken, device, "1985-05-05");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("\"requiresOnboarding\":false", await second.Content.ReadAsStringAsync());

        Assert.Equal("1990-01-01",
            await _api.ScalarAsync<string>("SELECT date_of_birth::text FROM users WHERE id = @v", google.UserId));
    }

    [Fact]
    public async Task Onboarding_Refusal_RevokesRefreshTokens_MarksTheAccount_AndStoresNoDate()
    {
        var device = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(device);

        var refusal = await _api.OnboardingAsync(google.AccessToken, device, AgeGateApi.UnderAgeBirthDate);

        await AgeGateApi.AssertErrorAsync(refusal, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.True(await _api.ScalarAsync<bool>(
            "SELECT age_gate_failed_at IS NOT NULL AND date_of_birth IS NULL FROM users WHERE id = @v", google.UserId));
        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @v AND revoked_at IS NULL", google.UserId));
        Assert.True(await _api.IsDeviceLockedAsync(device));

        var refresh = await _api.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = google.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task RefusedGoogleAccount_IsRefused_FromAFreshDevice_AtSignInAndAtOnboarding()
    {
        var device = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(device);
        var refusal = await _api.OnboardingAsync(google.AccessToken, device, AgeGateApi.UnderAgeBirthDate);
        Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);

        // Same Google account, fresh device: no tokens.
        var freshDevice = await _api.RegisterDeviceAsync();
        var signIn = await _api.GoogleSignInAsync(freshDevice, google.Uid, google.Email);
        await AgeGateApi.AssertErrorAsync(signIn, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @v AND revoked_at IS NULL", google.UserId));

        // The still-valid access token cannot complete onboarding with an adult date either.
        var retry = await _api.OnboardingAsync(google.AccessToken, freshDevice, AgeGateApi.AdultBirthDate);
        await AgeGateApi.AssertErrorAsync(retry, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.True(await _api.ScalarAsync<bool>("SELECT date_of_birth IS NULL FROM users WHERE id = @v", google.UserId));
    }

    [Fact]
    public async Task RefusedPasswordAccount_GetsNoTokensFromLogin()
    {
        // A password account created before V31 has no date of birth.
        var device = await _api.RegisterDeviceAsync();
        var username = AgeGateApi.NewUsername();
        await _api.RegisterAndVerifyAsync(device, username);
        await _api.ExecuteAsync("UPDATE users SET date_of_birth = NULL WHERE username = @v", username);

        var login = await _api.LoginAsync(device, username, AgeGateApi.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.True(loginJson.RootElement.GetProperty("requiresOnboarding").GetBoolean());
        var accessToken = loginJson.RootElement.GetProperty("accessToken").GetString()!;

        var refusal = await _api.OnboardingAsync(accessToken, device, AgeGateApi.UnderAgeBirthDate);
        Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);

        var freshDevice = await _api.RegisterDeviceAsync();
        var again = await _api.LoginAsync(freshDevice, username, AgeGateApi.Password);
        await AgeGateApi.AssertErrorAsync(again, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);

        // Only someone who proves the password learns about the refusal.
        var wrongPassword = await _api.LoginAsync(await _api.RegisterDeviceAsync(), username, "wrong-password-123");
        await AgeGateApi.AssertErrorAsync(wrongPassword, HttpStatusCode.BadRequest, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task GoogleSignUp_OnALockedDevice_Returns403_AndCreatesNoUserRow()
    {
        var device = await _api.RegisterDeviceAsync();
        var declaration = await _api.DeclareAgeAsync(device, AgeGateApi.UnderAgeBirthDate);
        Assert.Equal(HttpStatusCode.Forbidden, declaration.StatusCode);

        var uid = $"uid-{Guid.NewGuid():N}";
        var signUp = await _api.GoogleSignInAsync(device, uid, $"{uid}@example.test");

        await AgeGateApi.AssertErrorAsync(signUp, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.Equal(0L, await _api.ScalarAsync<long>("SELECT COUNT(*) FROM users WHERE google_id = @v", uid));
    }

    [Fact]
    public async Task Onboarding_FromAnUnlockedDevice_IsRefused_WhenTheAccountDeviceIsLocked()
    {
        var accountDevice = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(accountDevice);
        var declaration = await _api.DeclareAgeAsync(accountDevice, AgeGateApi.UnderAgeBirthDate);
        Assert.Equal(HttpStatusCode.Forbidden, declaration.StatusCode);

        var otherDevice = await _api.RegisterDeviceAsync();
        var onboarding = await _api.OnboardingAsync(google.AccessToken, otherDevice, AgeGateApi.AdultBirthDate);

        await AgeGateApi.AssertErrorAsync(onboarding, HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.True(await _api.ScalarAsync<bool>("SELECT date_of_birth IS NULL FROM users WHERE id = @v", google.UserId));
    }

    [Fact]
    public async Task FlagsOff_GuestPostCommentAndVote_StillWork()
    {
        var author = await _api.RegisterDeviceAsync();
        var postId = await _api.CreatePostAsync(author, accessToken: null);

        var voter = await _api.RegisterDeviceAsync();
        var vote = await _api.VoteAsync(voter, postId, accessToken: null);
        Assert.Equal(HttpStatusCode.OK, vote.StatusCode);

        var comment = await _api.CommentAsync(voter, postId, accessToken: null);
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
    }

    [Fact]
    public async Task AdminReportQueue_ListsMinorReportsBeforeRoutineOnes()
    {
        var author = await _api.RegisterDeviceAsync();
        var postId = await _api.CreatePostAsync(author, accessToken: null);
        var spam = await _api.ReportAsync(await _api.RegisterDeviceAsync(), postId, "spam");
        var minor = await _api.ReportAsync(await _api.RegisterDeviceAsync(), postId, "minor");

        var adminToken = await _api.AdminLoginAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/reports?status=pending&limit=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await _api.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        var minorIndex = ids.IndexOf(minor);
        var spamIndex = ids.IndexOf(spam);
        Assert.True(minorIndex >= 0, "the newer 'minor' report is on the first page");
        Assert.True(spamIndex < 0 || spamIndex > minorIndex, "'minor' is listed before the older 'spam' report");

        // Queue priority only: two reports do not hide the post.
        Assert.NotEqual("under_review", await _api.ScalarAsync<string>("SELECT status FROM posts WHERE id = @v", postId));
    }
}

[Trait("Category", "RequiresDb")]
public sealed class AgeGateEnforceTests(AgeGateEnforceFactory factory) : IClassFixture<AgeGateEnforceFactory>
{
    private readonly AgeGateApi _api = new(factory);

    [Fact]
    public async Task Enforce_AccountWithoutBirthDate_Gets403_UntilOnboarding()
    {
        var device = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(device);

        var refused = await _api.SendCreatePostAsync(device, google.AccessToken);
        await AgeGateApi.AssertErrorAsync(refused, HttpStatusCode.Forbidden, AgeGateCodes.VerificationRequired);

        var onboarding = await _api.OnboardingAsync(google.AccessToken, device, AgeGateApi.AdultBirthDate);
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);

        var created = await _api.SendCreatePostAsync(device, google.AccessToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Enforce_ProfileEditAndGuestDataMigration_AreGated()
    {
        var device = await _api.RegisterDeviceAsync();
        var google = await _api.GoogleSignUpAsync(device);

        using (var edit = new HttpRequestMessage(HttpMethod.Put, "/api/v1/users/me"))
        {
            edit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", google.AccessToken);
            edit.Content = JsonContent.Create(new { bio = "merhaba" });
            await AgeGateApi.AssertErrorAsync(
                await _api.Client.SendAsync(edit), HttpStatusCode.Forbidden, AgeGateCodes.VerificationRequired);
        }

        using var migrate = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/migrate-guest-data");
        migrate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", google.AccessToken);
        migrate.Headers.Add("X-Device-Token", device);
        await AgeGateApi.AssertErrorAsync(
            await _api.Client.SendAsync(migrate), HttpStatusCode.Forbidden, AgeGateCodes.VerificationRequired);
    }

    [Fact]
    public async Task Enforce_GuestPostAndComment_WithoutJwt_Get401AuthRequired()
    {
        var authorDevice = await _api.RegisterDeviceAsync();
        var author = await _api.RegisterAndVerifyAsync(authorDevice, AgeGateApi.NewUsername());
        var postId = await _api.CreatePostAsync(authorDevice, author);

        var guest = await _api.RegisterDeviceAsync();
        await AgeGateApi.AssertErrorAsync(
            await _api.SendCreatePostAsync(guest, accessToken: null), HttpStatusCode.Unauthorized, AgeGateCodes.SignInRequired);
        await AgeGateApi.AssertErrorAsync(
            await _api.CommentAsync(guest, postId, accessToken: null), HttpStatusCode.Unauthorized, AgeGateCodes.SignInRequired);
        await AgeGateApi.AssertErrorAsync(
            await _api.CommentAsync(guest, postId, accessToken: "not-a-jwt"), HttpStatusCode.Unauthorized, AgeGateCodes.SignInRequired);

        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT COUNT(*) FROM posts p JOIN devices d ON d.id = p.device_id WHERE d.device_token = @v", guest));
        Assert.Equal(0L, await _api.ScalarAsync<long>(
            "SELECT COUNT(*) FROM comments c JOIN devices d ON d.id = c.device_id WHERE d.device_token = @v", guest));
    }

    [Fact]
    public async Task Enforce_LockedDevice_GuestVoteIsRefused_WhileOtherGuestsCanStillVote()
    {
        var authorDevice = await _api.RegisterDeviceAsync();
        var author = await _api.RegisterAndVerifyAsync(authorDevice, AgeGateApi.NewUsername());
        var postId = await _api.CreatePostAsync(authorDevice, author);

        var locked = await _api.RegisterDeviceAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.DeclareAgeAsync(locked, AgeGateApi.UnderAgeBirthDate)).StatusCode);
        await AgeGateApi.AssertErrorAsync(
            await _api.VoteAsync(locked, postId, accessToken: null), HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);

        var guest = await _api.RegisterDeviceAsync();
        Assert.Equal(HttpStatusCode.OK, (await _api.VoteAsync(guest, postId, accessToken: null)).StatusCode);
    }

    [Fact]
    public async Task Enforce_AdultAccountOnALockedDevice_Gets403NotEligible()
    {
        var accountDevice = await _api.RegisterDeviceAsync();
        var adult = await _api.RegisterAndVerifyAsync(accountDevice, AgeGateApi.NewUsername());

        var locked = await _api.RegisterDeviceAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.DeclareAgeAsync(locked, AgeGateApi.UnderAgeBirthDate)).StatusCode);

        await AgeGateApi.AssertErrorAsync(
            await _api.SendCreatePostAsync(locked, adult), HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        Assert.Equal(HttpStatusCode.Created, (await _api.SendCreatePostAsync(accountDevice, adult)).StatusCode);
    }
}

[Trait("Category", "RequiresDb")]
public sealed class AgeGateGuestVoteTests(AgeGateGuestVoteFactory factory) : IClassFixture<AgeGateGuestVoteFactory>
{
    private readonly AgeGateApi _api = new(factory);

    [Fact]
    public async Task GuestVote_NeedsTheNeutralDeclaration_AndARefusedDeviceStaysRefused()
    {
        var author = await _api.RegisterDeviceAsync();
        var postId = await _api.CreatePostAsync(author, accessToken: null);

        var guest = await _api.RegisterDeviceAsync();
        await AgeGateApi.AssertErrorAsync(
            await _api.VoteAsync(guest, postId, accessToken: null), HttpStatusCode.Forbidden, AgeGateCodes.DeclarationRequired);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeclareAgeAsync(guest, AgeGateApi.AdultBirthDate)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.VoteAsync(guest, postId, accessToken: null)).StatusCode);

        var minor = await _api.RegisterDeviceAsync();
        await AgeGateApi.AssertErrorAsync(
            await _api.DeclareAgeAsync(minor, AgeGateApi.UnderAgeBirthDate), HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        await AgeGateApi.AssertErrorAsync(
            await _api.DeclareAgeAsync(minor, AgeGateApi.AdultBirthDate), HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
        await AgeGateApi.AssertErrorAsync(
            await _api.VoteAsync(minor, postId, accessToken: null), HttpStatusCode.Forbidden, AgeGateCodes.NotEligible);
    }
}

// ── Hosts ────────────────────────────────────────────────────────────────────

public abstract class AgeGateFactory(bool enforce, bool guestVote) : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgeGate:Enforce"] = enforce ? "true" : "false",
            ["AgeGate:GuestVote"] = guestVote ? "true" : "false",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FirebaseAuthService>();
            services.AddSingleton<FirebaseAuthService, FakeGoogleTokenVerifier>();
        });
    }
}

public sealed class AgeGateFlagsOffFactory() : AgeGateFactory(enforce: false, guestVote: false)
{
}

public sealed class AgeGateEnforceFactory() : AgeGateFactory(enforce: true, guestVote: false)
{
}

public sealed class AgeGateGuestVoteFactory() : AgeGateFactory(enforce: false, guestVote: true)
{
}

// Accepts "fake-google-id-token:<uid>:<email>" and nothing else. FirebaseToken has no public
// constructor, so the token is built through FirebaseAdmin's internal Args type; if a
// FirebaseAdmin upgrade changes it, this throws loudly instead of passing silently.
public sealed class FakeGoogleTokenVerifier(IConfiguration configuration, ILogger<FirebaseAuthService> logger)
    : FirebaseAuthService(configuration, logger)
{
    private const string Prefix = "fake-google-id-token:";

    public static string IdTokenFor(string uid, string email) => $"{Prefix}{uid}:{email}";

    public override Task<FirebaseToken?> VerifyIdTokenAsync(string idToken)
    {
        if (!idToken.StartsWith(Prefix, StringComparison.Ordinal)) return Task.FromResult<FirebaseToken?>(null);
        var parts = idToken[Prefix.Length..].Split(':', 2);
        return Task.FromResult<FirebaseToken?>(CreateToken(parts[0], parts[1]));
    }

    private static FirebaseToken CreateToken(string uid, string email)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var argsType = typeof(FirebaseToken).GetNestedType("Args", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FirebaseToken.Args not found: FirebaseAdmin internals changed.");
        var args = Activator.CreateInstance(argsType, nonPublic: true)!;
        argsType.GetProperty("Subject", Any)!.SetValue(args, uid);
        argsType.GetProperty("Claims", Any)!.SetValue(args, new Dictionary<string, object>
        {
            ["email"] = email,
            ["email_verified"] = true,
        });
        var constructor = typeof(FirebaseToken).GetConstructor(Any, [argsType])
            ?? throw new InvalidOperationException("FirebaseToken(Args) not found: FirebaseAdmin internals changed.");
        return (FirebaseToken)constructor.Invoke([args]);
    }
}

// ── API helpers ──────────────────────────────────────────────────────────────

public sealed record GoogleSession(Guid UserId, string Uid, string Email, string AccessToken, string RefreshToken, bool RequiresOnboarding);

public sealed class AgeGateApi(CustomWebApplicationFactory factory)
{
    public const string Password = "long-enough-password";
    public const string AdultBirthDate = "1990-01-01";

    // Fifteen years ago: ineligible on any day.
    public static string UnderAgeBirthDate => DateTime.UtcNow.AddYears(-15).ToString("yyyy-MM-dd");

    public HttpClient Client { get; } = factory.CreateClient();

    public static string NewUsername() => $"ag_{Guid.NewGuid():N}"[..20];

    public static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    public async Task<string> RegisterDeviceAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/devices/register", new
        {
            fingerprint = $"it-{Guid.NewGuid():N}",
            platform = "web",
            appVersion = "1.0.0",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("deviceToken").GetString()!;
    }

    public async Task<HttpResponseMessage> RegisterAsync(string deviceToken, string username, string dateOfBirth)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username,
                email = $"{username}@example.test",
                password = Password,
                dateOfBirth,
                gender = "prefer_not_to_say",
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
            }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        return await Client.SendAsync(request);
    }

    // Registers an adult password account, verifies the e-mail and returns its access token.
    public async Task<string> RegisterAndVerifyAsync(string deviceToken, string username)
    {
        var registered = await RegisterAsync(deviceToken, username, AdultBirthDate);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        // No e-mail provider in tests: the register handler keeps "<ts>|<hash>|<otp>" in Redis.
        var email = $"{username}@example.test";
        var cached = (string?)await factory.Services.GetRequiredService<RedisService>().GetDb()
            .StringGetAsync($"otp:verify:{email}");
        Assert.NotNull(cached);

        using var verify = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/verify-email")
        {
            Content = JsonContent.Create(new { email, otp = cached!.Split('|')[2] }),
        };
        verify.Headers.Add("X-Device-Token", deviceToken);
        var verified = await Client.SendAsync(verify);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var json = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    public async Task<HttpResponseMessage> LoginAsync(string deviceToken, string identifier, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { identifier, password }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> GoogleSignInAsync(string deviceToken, string uid, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google")
        {
            Content = JsonContent.Create(new { idToken = FakeGoogleTokenVerifier.IdTokenFor(uid, email) }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        return await Client.SendAsync(request);
    }

    // A new Google account: no birth date until the onboarding step.
    public async Task<GoogleSession> GoogleSignUpAsync(string deviceToken)
    {
        var uid = $"uid-{Guid.NewGuid():N}";
        var email = $"{uid}@example.test";
        var response = await GoogleSignInAsync(deviceToken, uid, email);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"google sign-up: {(int)response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new GoogleSession(
            root.GetProperty("userId").GetGuid(),
            uid,
            email,
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            root.GetProperty("requiresOnboarding").GetBoolean());
    }

    public async Task<HttpResponseMessage> OnboardingAsync(string accessToken, string deviceToken, string dateOfBirth)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/me/onboarding")
        {
            Content = JsonContent.Create(new { dateOfBirth, acceptedTerms = true, acceptedCommunityGuidelines = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Device-Token", deviceToken);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> DeclareAgeAsync(string deviceToken, string dateOfBirth)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices/age-declaration")
        {
            Content = JsonContent.Create(new { dateOfBirth }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendCreatePostAsync(string deviceToken, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts")
        {
            Content = JsonContent.Create(new
            {
                title = "Yas kapisi entegrasyon testi",
                content = "Yas kapisi entegrasyon testi icin yazilmis, elli karakterden uzun bir gonderi icerigi.",
                categoryId = 1,
                acceptedTerms = true,
                acceptedCommunityGuidelines = true,
            }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    public async Task<Guid> CreatePostAsync(string deviceToken, string? accessToken)
    {
        var response = await SendCreatePostAsync(deviceToken, accessToken);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"create post: {(int)response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public async Task<HttpResponseMessage> VoteAsync(string deviceToken, Guid postId, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/posts/{postId}/vote")
        {
            Content = JsonContent.Create(new { voteType = "hakli" }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> CommentAsync(string deviceToken, Guid postId, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/posts/{postId}/comments")
        {
            Content = JsonContent.Create(new { content = "Yas kapisi entegrasyon testi yorumu." }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    public async Task<Guid> ReportAsync(string deviceToken, Guid postId, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports")
        {
            Content = JsonContent.Create(new { targetType = "post", targetId = postId, reason }),
        };
        request.Headers.Add("X-Device-Token", deviceToken);
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"report: {(int)response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("reportId").GetGuid();
    }

    public async Task<string> AdminLoginAsync()
    {
        // Testing environment without an e-mail provider: credentials alone return a token.
        var response = await Client.PostAsJsonAsync("/api/v1/admin/auth/login", new
        {
            email = "admin@test.local",
            password = "test-admin-password",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    public async Task<bool> IsDeviceLockedAsync(string deviceToken) =>
        await ScalarAsync<bool>("SELECT age_gate_failed_at IS NOT NULL FROM devices WHERE device_token = @v", deviceToken);

    public async Task<T> ScalarAsync<T>(string sql, object value)
    {
        await using var connection = await factory.Services.GetRequiredService<Db>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("v", value);
        var result = await command.ExecuteScalarAsync();
        return result is T typed ? typed : (T)Convert.ChangeType(result!, typeof(T));
    }

    public async Task ExecuteAsync(string sql, object value)
    {
        await using var connection = await factory.Services.GetRequiredService<Db>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("v", value);
        await command.ExecuteNonQueryAsync();
    }
}
