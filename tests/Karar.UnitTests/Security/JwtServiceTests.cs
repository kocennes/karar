using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Karar.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Karar.UnitTests.Security;

public sealed class JwtServiceTests
{
    [Fact]
    public void ValidateAccessToken_AcceptsCurrentAndPreviousRsaKeys()
    {
        using var current = RSA.Create(2048);
        using var previous = RSA.Create(2048);
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "karar-api",
            ["Jwt:Audience"] = "karar-mobile",
            ["Jwt:PrivateKeyPem"] = current.ExportRSAPrivateKeyPem(),
            ["Jwt:PublicKeyPem"] = current.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:KeyId"] = "current-2026-05",
            ["Jwt:PreviousPublicKeyPem"] = previous.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:PreviousKeyId"] = "previous-2025-11",
        });

        var service = new JwtService(configuration);
        var currentToken = service.GenerateAccessToken(Guid.NewGuid(), "ada");
        var previousToken = GenerateToken(previous, "previous-2025-11");

        service.ValidateAccessToken(currentToken).Should().NotBeNull();
        service.ValidateAccessToken(previousToken).Should().NotBeNull();
    }

    [Fact]
    public void GetJwks_ReturnsCurrentAndPreviousPublicKeys()
    {
        using var current = RSA.Create(2048);
        using var previous = RSA.Create(2048);
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:PrivateKeyPem"] = current.ExportRSAPrivateKeyPem(),
            ["Jwt:PublicKeyPem"] = current.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:KeyId"] = "current-2026-05",
            ["Jwt:PreviousPublicKeyPem"] = previous.ExportSubjectPublicKeyInfoPem(),
            ["Jwt:PreviousKeyId"] = "previous-2025-11",
        });

        var jwksJson = System.Text.Json.JsonSerializer.Serialize(
            new JwtService(configuration).GetJwks());

        jwksJson.Should().Contain("current-2026-05");
        jwksJson.Should().Contain("previous-2025-11");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string GenerateToken(RSA rsa, string keyId)
    {
        var key = new RsaSecurityKey(rsa) { KeyId = keyId };
        var token = new JwtSecurityToken(
            issuer: "karar-api",
            audience: "karar-mobile",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, "ada"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
