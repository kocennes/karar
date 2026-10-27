using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Karar.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Karar.UnitTests.Security;

/// Behavioural tests for admin authentication: only signed, unexpired JWTs with
/// role=admin are accepted; there is no static/shared admin token anymore.
public sealed class AdminAuthServiceTests
{
    private const string AdminEmail = "admin@test.local";
    private const string JwtSecret = "unit-test-secret-minimum-32-characters-long";
    private const string Issuer = "karar-api";
    private const string Audience = "karar-mobile";

    [Fact]
    public void IssueToken_ReturnsJwtAcceptedByTryGetAdminEmail_ViaBearerHeader()
    {
        var (adminAuth, _) = CreateServices();

        var login = adminAuth.IssueToken();

        login.AccessToken.Split('.').Should().HaveCount(3, "admin token must be a JWT");
        adminAuth.TryGetAdminEmail(BearerRequest(login.AccessToken)).Should().Be(AdminEmail);
    }

    [Fact]
    public void IssueToken_ReturnsJwtAcceptedByTryGetAdminEmail_ViaAdminTokenHeader()
    {
        var (adminAuth, _) = CreateServices();

        var login = adminAuth.IssueToken();

        adminAuth.TryGetAdminEmail(AdminHeaderRequest(login.AccessToken)).Should().Be(AdminEmail);
    }

    [Fact]
    public void GenerateAdminToken_IsAcceptedByTryGetAdminEmail()
    {
        var (adminAuth, jwt) = CreateServices();

        var (token, _) = jwt.GenerateAdminToken(AdminEmail);

        adminAuth.TryGetAdminEmail(BearerRequest(token)).Should().Be(AdminEmail);
    }

    [Fact]
    public void IssueToken_WorksWithRs256SigningKeys()
    {
        using var rsa = RSA.Create(2048);
        var (adminAuth, _) = CreateServices(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = null,
            ["Jwt:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
            ["Jwt:PublicKeyPem"] = rsa.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:KeyId"] = "current-2026-10",
        });

        var login = adminAuth.IssueToken();

        adminAuth.TryGetAdminEmail(BearerRequest(login.AccessToken)).Should().Be(AdminEmail);
    }

    [Fact]
    public void IssueToken_ExpiresAtMatchesJwtExpClaim()
    {
        var (adminAuth, _) = CreateServices();
        var before = DateTimeOffset.UtcNow;

        var login = adminAuth.IssueToken();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        login.ExpiresAt.UtcDateTime.Should().Be(jwt.ValidTo,
            "login response expiresAt must come from the same value written to the token");
        login.ExpiresAt.Should().BeCloseTo(before.Add(JwtService.AdminTokenLifetime), TimeSpan.FromSeconds(5));
        JwtService.AdminTokenLifetime.Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public void IssueToken_IssuesDistinctTokenPerLogin()
    {
        var (adminAuth, _) = CreateServices();

        var first = adminAuth.IssueToken().AccessToken;
        var second = adminAuth.IssueToken().AccessToken;

        first.Should().NotBe(second, "every login must get its own token (unique jti)");
    }

    [Fact]
    public void TryGetAdminEmail_RejectsNormalUserAccessToken()
    {
        var (adminAuth, jwt) = CreateServices();

        var userToken = jwt.GenerateAccessToken(Guid.NewGuid(), "ada");

        jwt.ValidateAccessToken(userToken).Should().NotBeNull("the user token itself is valid");
        adminAuth.TryGetAdminEmail(BearerRequest(userToken)).Should().BeNull();
        adminAuth.TryGetAdminEmail(AdminHeaderRequest(userToken)).Should().BeNull();
    }

    [Fact]
    public void TryGetAdminEmail_AcceptsCorrectlySignedAdminShapedToken_ControlCase()
    {
        // Control for the expired-token test: the same builder with a future expiry is accepted,
        // so the rejection below is caused by expiry alone.
        var (adminAuth, _) = CreateServices();

        var token = BuildAdminShapedToken(JwtSecret, DateTime.UtcNow.AddMinutes(10));

        adminAuth.TryGetAdminEmail(BearerRequest(token)).Should().Be(AdminEmail);
    }

    [Fact]
    public void TryGetAdminEmail_RejectsExpiredAdminToken()
    {
        var (adminAuth, _) = CreateServices();

        var token = BuildAdminShapedToken(JwtSecret, DateTime.UtcNow.AddMinutes(-5));

        adminAuth.TryGetAdminEmail(BearerRequest(token)).Should().BeNull();
    }

    [Fact]
    public void TryGetAdminEmail_RejectsAdminTokenSignedWithForeignKey()
    {
        var (adminAuth, _) = CreateServices();

        var forged = BuildAdminShapedToken(
            "attacker-controlled-secret-at-least-32-characters",
            DateTime.UtcNow.AddMinutes(10));

        adminAuth.TryGetAdminEmail(BearerRequest(forged)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJyb2xlIjoiYWRtaW4ifQ.")]
    public void TryGetAdminEmail_RejectsGarbageTokens(string token)
    {
        var (adminAuth, _) = CreateServices();

        adminAuth.TryGetAdminEmail(BearerRequest(token)).Should().BeNull();
        adminAuth.TryGetAdminEmail(AdminHeaderRequest(token)).Should().BeNull();
    }

    [Fact]
    public void TryGetAdminEmail_RejectsRequestWithoutToken()
    {
        var (adminAuth, _) = CreateServices();

        adminAuth.TryGetAdminEmail(new DefaultHttpContext().Request).Should().BeNull();
    }

    [Fact]
    public void ConfiguredStaticAdminToken_IsNoLongerAccepted()
    {
        const string legacyToken = "legacy-static-admin-token";
        var (adminAuth, _) = CreateServices(new Dictionary<string, string?>
        {
            ["Admin:Token"] = legacyToken,
        });

        adminAuth.TryGetAdminEmail(AdminHeaderRequest(legacyToken)).Should().BeNull();
        adminAuth.TryGetAdminEmail(BearerRequest(legacyToken)).Should().BeNull();

        var login = adminAuth.IssueToken();
        login.AccessToken.Should().NotBe(legacyToken, "login must never hand out the configured static token");
        adminAuth.TryGetAdminEmail(BearerRequest(login.AccessToken)).Should().Be(AdminEmail);
    }

    [Fact]
    public void TryGetAdminEmail_KeepsIpAllowlistEnforcement()
    {
        var (adminAuth, _) = CreateServices(new Dictionary<string, string?>
        {
            ["Admin:IpAllowlist"] = "10.0.0.1",
        });
        var token = adminAuth.IssueToken().AccessToken;

        adminAuth.TryGetAdminEmail(BearerRequest(token, IPAddress.Parse("10.0.0.1"))).Should().Be(AdminEmail);
        adminAuth.TryGetAdminEmail(BearerRequest(token, IPAddress.Parse("10.0.0.2"))).Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_UserToken_KeepsGetUserIdStyleSubExtraction()
    {
        var (_, jwt) = CreateServices();
        var userId = Guid.NewGuid();

        var principal = jwt.ValidateAccessToken(jwt.GenerateAccessToken(userId, "ada"));

        principal.Should().NotBeNull();
        // Same lookup as GetUserId() in Program.cs.
        var sub = principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid.Parse(sub!).Should().Be(userId);
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub).Should().Be(userId.ToString(),
            "inbound claim mapping is disabled, so the raw 'sub' claim must be present");
    }

    [Fact]
    public void ValidateAccessToken_UserToken_ExposesRawUniqueNameClaim()
    {
        // 2FA setup reads JwtRegisteredClaimNames.UniqueName for the otpauth label.
        var (_, jwt) = CreateServices();

        var validated = jwt.ValidateAccessToken(jwt.GenerateAccessToken(Guid.NewGuid(), "ada"));

        validated.Should().NotBeNull();
        validated!.FindFirstValue(JwtRegisteredClaimNames.UniqueName).Should().Be("ada");
    }

    [Fact]
    public void ValidateAccessToken_AdminToken_ExposesRawEmailAndRoleClaims()
    {
        var (_, jwt) = CreateServices();

        var validated = jwt.ValidateAccessToken(jwt.GenerateAdminToken(AdminEmail).Token);

        validated.Should().NotBeNull();
        var principal = validated!;
        principal.FindFirstValue(JwtRegisteredClaimNames.Email).Should().Be(AdminEmail);
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub).Should().Be(AdminEmail);
        principal.FindFirstValue(ClaimTypes.Role).Should().Be("admin");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void JwtService_RefusesShippedDefaultSecret_OutsideDevelopmentAndTesting(string environmentName)
    {
        // A token signed with the publicly known default secret would pass ValidateAdminToken,
        // so the default must never be the signing key of an internet-facing deployment.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = ReadShippedDefaultJwtSecret(),
        });

        var act = () => new JwtService(configuration, new TestWebHostEnvironment { EnvironmentName = environmentName });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void JwtService_RefusesShippedDefaultSecret_WhenEnvironmentIsUnknown()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = ReadShippedDefaultJwtSecret(),
        });

        var act = () => new JwtService(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void JwtService_AllowsShippedDefaultSecret_InDevelopmentAndTesting(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = ReadShippedDefaultJwtSecret(),
        });

        var act = () => new JwtService(configuration, new TestWebHostEnvironment { EnvironmentName = environmentName });

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Staging", true)]
    public void JwtService_ResolvedFromDi_ReceivesHostEnvironment(string environmentName, bool shouldThrow)
    {
        // Program.cs registers JwtService via AddSingleton<JwtService>(); the optional
        // IHostEnvironment parameter must be filled by the container, not left null.
        var services = new ServiceCollection();
        services.AddSingleton(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = ReadShippedDefaultJwtSecret(),
        }));
        services.AddSingleton<IHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = environmentName });
        services.AddSingleton<JwtService>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<JwtService>();

        if (shouldThrow)
            act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Secret*");
        else
            act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AdminAuthService_RefusesShippedDefaultPassword_OutsideDevelopmentAndTesting(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Admin:Password"] = "dev-admin-password",
            ["Jwt:Secret"] = JwtSecret,
        });

        var act = () => new AdminAuthService(
            configuration,
            new TestWebHostEnvironment { EnvironmentName = environmentName },
            new JwtService(configuration));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Admin:Password*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AdminAuthService_AllowsShippedDefaultPassword_InDevelopmentAndTesting(string environmentName)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Admin:Password"] = "dev-admin-password",
            ["Jwt:Secret"] = JwtSecret,
        });

        var act = () => new AdminAuthService(
            configuration,
            new TestWebHostEnvironment { EnvironmentName = environmentName },
            new JwtService(configuration));

        act.Should().NotThrow();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Reads the secret actually shipped in appsettings.json so the guard stays in sync with it.
    private static string ReadShippedDefaultJwtSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(TestRepoPaths.FilePath("backend", "Karar.Api", "appsettings.json"), optional: false)
            .Build();
        var secret = configuration["Jwt:Secret"];
        secret.Should().NotBeNullOrWhiteSpace("appsettings.json ships a placeholder Jwt:Secret");
        return secret!;
    }

    private static (AdminAuthService AdminAuth, JwtService Jwt) CreateServices(
        Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = "unit-test-admin-password",
            ["Jwt:Secret"] = JwtSecret,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
        };
        foreach (var (key, value) in overrides ?? [])
            values[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var jwt = new JwtService(configuration);
        return (new AdminAuthService(configuration, new TestWebHostEnvironment(), jwt), jwt);
    }

    private static HttpRequest BearerRequest(string token, IPAddress? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp ?? IPAddress.Loopback;
        context.Request.Headers.Authorization = $"Bearer {token}";
        return context.Request;
    }

    private static HttpRequest AdminHeaderRequest(string token)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Admin-Token"] = token;
        return context.Request;
    }

    // Mirrors JwtService.GenerateAdminToken claims so expiry/signature can be controlled.
    private static string BuildAdminShapedToken(string secret, DateTime expiresUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = "dev" };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, AdminEmail),
                new Claim(JwtRegisteredClaimNames.Email, AdminEmail),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            notBefore: expiresUtc.AddHours(-1),
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Karar.Api";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
