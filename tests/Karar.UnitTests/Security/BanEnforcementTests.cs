using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Common.Middleware;
using Karar.Api.Services;
using Karar.UnitTests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Karar.UnitTests.Security;

/// [F8] Kullanıcı ve cihaz banları uygulanmalı:
/// (1) Google ile giriş banlı/silinmiş hesaba token vermemeli,
/// (2) banlı/silinmiş kullanıcının elindeki access token yazma isteklerinde reddedilmeli,
/// (3) X-Device-Token düşürülüp yalnızca JWT gönderildiğinde banlı cihaza geri düşülmemeli,
/// (4) kullanıcı banı cihaza da uzanmalı (çıkış yapıp misafir olarak devam edilememeli) ve
///     strike ile gelen ban refresh token'ları iptal etmeli.
/// DB'ye bağlı yollar kaynak sözleşmesiyle doğrulanır.
public sealed class BanEnforcementTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── (1) Google sign-in ─────────────────────────────────────────────────

    [Fact]
    public void GoogleSignIn_RejectsBannedAndDeletedAccountsBeforeIssuingTokens()
    {
        var block = Slice(ProgramText, "app.MapPost(\"/api/v1/auth/google\"", "app.MapPost(\"/api/v1/auth/google/link\"");

        block.Should().MatchRegex(@"SELECT id, username, email, karma, auth_provider, is_banned, deleted_at",
            "the existing-user lookup must load the ban and deletion state");

        var bannedCheck = block.IndexOf("\"USER_BANNED\"", StringComparison.Ordinal);
        var deletedCheck = block.IndexOf("\"ACCOUNT_DELETED\"", StringComparison.Ordinal);
        var tokenIssue = block.IndexOf("jwtService.GenerateAccessToken(", StringComparison.Ordinal);

        bannedCheck.Should().BeGreaterThan(0, "a banned Google user must be refused like password login does");
        deletedCheck.Should().BeGreaterThan(0, "a soft-deleted account must not be revived by Google sign-in");
        tokenIssue.Should().BeGreaterThan(bannedCheck, "the ban check must run before tokens are issued");
        tokenIssue.Should().BeGreaterThan(deletedCheck, "the deletion check must run before tokens are issued");
    }

    // ── (2) Request-level principal check ──────────────────────────────────

    [Fact]
    public void BannedUserMiddleware_IsRegisteredAfterAdminSecurity()
    {
        var adminPos = ProgramText.IndexOf("UseMiddleware<AdminSecurityMiddleware>()", StringComparison.Ordinal);
        var bannedPos = ProgramText.IndexOf("UseMiddleware<BannedUserMiddleware>()", StringComparison.Ordinal);

        bannedPos.Should().BeGreaterThan(0, "a still-valid access token of a banned user must be checked per request");
        bannedPos.Should().BeGreaterThan(adminPos, "the ban check runs after the existing security middlewares");
        ProgramText.Should().Contain("AddSingleton<IUserAccessStatusService, UserAccessStatusService>()");
    }

    [Fact]
    public void UserAccessStatusService_ReadsBanAndDeletionStateWithShortCache()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "UserAccessStatusService.cs");

        service.Should().Contain("SELECT is_banned, deleted_at FROM users WHERE id = @userId");
        service.Should().Contain("CacheTtl = TimeSpan.FromSeconds(30)",
            "ban state is cached briefly in Redis so every write does not hit Postgres");
    }

    // ── (3) JWT-only fallback to a banned device ───────────────────────────

    [Fact]
    public void GetUserDeviceIdAsync_SkipsBannedDevicesUnlessExplicitlyRequested()
    {
        var block = Slice(ProgramText, "static async Task<Guid?> GetUserDeviceIdAsync(", "static async Task<bool> IsPostOwnerAsync(");

        block.Should().Contain("JOIN devices d ON d.id = u.device_id",
            "the JWT fallback must look at the device's ban flag");
        block.Should().Contain("d.is_banned = FALSE",
            "a banned device must not be used when the client drops X-Device-Token and sends only a JWT");
        block.Should().Contain("bool includeBannedDevice = false",
            "excluding banned devices must be the default for every caller");
    }

    [Theory]
    [InlineData("app.MapPost(\"/api/v1/posts\", async (", "app.MapGet(\"/api/v1/posts/{id:guid}\", async (")]
    [InlineData("app.MapPut(\"/api/v1/posts/{id:guid}\"", "app.MapPost(\"/api/v1/posts/{id:guid}/save\"")]
    [InlineData("app.MapPost(\"/api/v1/posts/{id:guid}/vote\"", "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"")]
    [InlineData("app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"", "app.MapGet(\"/api/v1/posts/{id:guid}/events\"")]
    [InlineData("app.MapPost(\"/api/v1/posts/{id:guid}/comments\"", "app.MapDelete(\"/api/v1/comments/{id:guid}\"")]
    public void WriteEndpoints_UseBannedDeviceExcludingFallback(string startMarker, string endMarker)
    {
        var block = Slice(ProgramText, startMarker, endMarker);

        block.Should().Contain("GetUserDeviceIdAsync(", "the write path resolves the device from the JWT user");
        block.Should().NotContain("includeBannedDevice: true",
            "write paths must never fall back to a banned device");
    }

    [Theory]
    [InlineData("app.MapGet(\"/api/v1/users/me/moderation-history\"", "app.MapGet(\"/api/v1/users/me/reports\"")]
    [InlineData("app.MapGet(\"/api/v1/users/me/reports\"", "app.MapPost(\"/api/v1/users/me/moderation-appeals\"")]
    [InlineData("app.MapPost(\"/api/v1/users/me/moderation-appeals\"", "app.MapPost(\"/api/v1/users/me/blocked\"")]
    [InlineData("app.MapGet(\"/api/v1/users/me/saved\"", "app.MapPut(\"/api/v1/users/me/password\"")]
    public void ReadOnlyAndAppealEndpoints_StillResolveBannedDevices(string startMarker, string endMarker)
    {
        var block = Slice(ProgramText, startMarker, endMarker);

        block.Should().Contain("includeBannedDevice: true",
            "a device-banned user must still be able to read their own history and appeal moderation");
    }

    // ── (4) User ban reaches the device; strike bans revoke sessions ───────

    [Fact]
    public void RequestDevice_RejectsTokenOfDeviceWhoseUserIsBanned()
    {
        var requestDevice = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "RequestDevice.cs");

        Regex.Replace(requestDevice, @"\s+", " ").Should().Contain(
            "NOT EXISTS ( SELECT 1 FROM users u WHERE u.device_id = devices.id AND u.is_banned = TRUE )",
            "a banned user must not keep posting as a guest from the device the account was registered on");
    }

    [Fact]
    public void StrikeEscalationBan_RevokesRefreshTokens()
    {
        var block = Slice(ProgramText, "app.MapPost(\"/api/v1/admin/users/{id:guid}/strike\"", "// ── ADMIN KVKK DELETE");

        block.Should().Contain("if (autoAction is not null)");
        block.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @userId AND revoked_at IS NULL",
            "a strike that escalates to a ban must end existing sessions like the admin user ban does");
    }

    // ── (2) Behavioural: BannedUserMiddleware ──────────────────────────────

    [Theory]
    [InlineData("POST", "/api/v1/posts")]
    [InlineData("POST", "/api/v1/posts/7c1e7c4e-0000-0000-0000-000000000001/vote")]
    [InlineData("POST", "/api/v1/posts/7c1e7c4e-0000-0000-0000-000000000001/comments")]
    [InlineData("PUT", "/api/v1/posts/7c1e7c4e-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/v1/posts/7c1e7c4e-0000-0000-0000-000000000001/vote")]
    [InlineData("POST", "/api/v1/reports")]
    [InlineData("PUT", "/api/v1/users/me")]
    public async Task Middleware_BannedUserWithValidAccessToken_CannotWrite(string method, string path)
    {
        var userId = Guid.NewGuid();
        var jwt = CreateJwtService();
        var status = new FakeAccessStatus(UserAccessState.Banned);
        var context = CreateContext(method, path, jwt.GenerateAccessToken(userId, "ada"));
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt, status)
            .InvokeAsync(context);

        reachedEndpoint.Should().BeFalse("a banned user's still-valid access token must not reach write endpoints");
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ReadBody(context).Should().Contain("\"code\":\"USER_BANNED\"");
        status.Queried.Should().Equal(userId);
    }

    [Fact]
    public async Task Middleware_DeletedUserWithValidAccessToken_IsTreatedAsUnauthenticated()
    {
        var userId = Guid.NewGuid();
        var jwt = CreateJwtService();
        var context = CreateContext("POST", "/api/v1/posts", jwt.GenerateAccessToken(userId, "ada"));
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt,
            new FakeAccessStatus(UserAccessState.Deleted)).InvokeAsync(context);

        reachedEndpoint.Should().BeFalse("a soft-deleted account must not keep writing with its last access token");
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Middleware_ActiveUserWrite_PassesThrough()
    {
        var userId = Guid.NewGuid();
        var jwt = CreateJwtService();
        var status = new FakeAccessStatus(UserAccessState.Active);
        var context = CreateContext("POST", "/api/v1/posts", jwt.GenerateAccessToken(userId, "ada"));
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt, status)
            .InvokeAsync(context);

        reachedEndpoint.Should().BeTrue();
        status.Queried.Should().Equal(userId);
    }

    [Theory]
    [InlineData("GET", "/api/v1/posts")]
    [InlineData("OPTIONS", "/api/v1/posts")]
    [InlineData("DELETE", "/api/v1/users/me")]
    [InlineData("POST", "/api/v1/users/me/moderation-appeals")]
    [InlineData("POST", "/api/v1/auth/logout")]
    [InlineData("POST", "/api/v1/auth/google")]
    [InlineData("POST", "/api/v1/devices/register")]
    public async Task Middleware_BannedUser_KeepsReadsAccountDeletionAppealsAndAuthEndpoints(string method, string path)
    {
        var jwt = CreateJwtService();
        var status = new FakeAccessStatus(UserAccessState.Banned);
        var context = CreateContext(method, path, jwt.GenerateAccessToken(Guid.NewGuid(), "ada"));
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt, status)
            .InvokeAsync(context);

        reachedEndpoint.Should().BeTrue("reads, KVKK deletion, appeals and token endpoints (which check bans themselves) stay reachable");
        status.Queried.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    public async Task Middleware_RequestsWithoutValidUserToken_AreNotChecked(string? token)
    {
        var jwt = CreateJwtService();
        var status = new FakeAccessStatus(UserAccessState.Banned);
        var context = CreateContext("POST", "/api/v1/posts", token);
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt, status)
            .InvokeAsync(context);

        reachedEndpoint.Should().BeTrue("anonymous and device-only requests are handled by RequestDevice, not here");
        status.Queried.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_AdminToken_IsNotTreatedAsUser()
    {
        var jwt = CreateJwtService();
        var status = new FakeAccessStatus(UserAccessState.Banned);
        var (adminToken, _) = jwt.GenerateAdminToken("admin@test.local");
        var context = CreateContext("POST", "/api/v1/admin/users/7c1e7c4e-0000-0000-0000-000000000001/ban", adminToken);
        var reachedEndpoint = false;

        await new BannedUserMiddleware(_ => { reachedEndpoint = true; return Task.CompletedTask; }, jwt, status)
            .InvokeAsync(context);

        reachedEndpoint.Should().BeTrue();
        status.Queried.Should().BeEmpty("admin tokens carry an e-mail subject, not a user id");
    }

    [Theory]
    [InlineData(true, false, false, UserAccessState.Active)]
    [InlineData(true, true, false, UserAccessState.Banned)]
    [InlineData(true, false, true, UserAccessState.Deleted)]
    [InlineData(true, true, true, UserAccessState.Deleted)]
    [InlineData(false, false, false, UserAccessState.Deleted)]
    public void UserAccessStatus_Evaluate_MapsBanAndDeletionState(
        bool userExists, bool isBanned, bool isDeleted, UserAccessState expected)
    {
        UserAccessStatusService.Evaluate(userExists, isBanned, isDeleted).Should().Be(expected);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static JwtService CreateJwtService() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "unit-test-secret-minimum-32-characters-long",
            ["Jwt:Issuer"] = "karar-api",
            ["Jwt:Audience"] = "karar-mobile",
        }).Build());

    private static DefaultHttpContext CreateContext(string method, string path, string? bearerToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (bearerToken is not null)
            context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    private sealed class FakeAccessStatus(UserAccessState state) : IUserAccessStatusService
    {
        public List<Guid> Queried { get; } = [];

        public Task<UserAccessState> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Queried.Add(userId);
            return Task.FromResult(state);
        }
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' must exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' must appear after '{startMarker}'");
        return text[start..end];
    }
}
