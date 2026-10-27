using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Karar.Api.Services;

public sealed class JwtService
{
    private readonly SecurityKey _signingKey;
    private readonly IReadOnlyList<SecurityKey> _validationKeys;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _algorithm;
    private readonly TimeSpan _accessTokenLifetime = TimeSpan.FromMinutes(15);

    // Admin oturum suresi. Login yanitindaki expiresAt da bu degerden uretilir (tek kaynak).
    public static readonly TimeSpan AdminTokenLifetime = TimeSpan.FromHours(4);

    // appsettings.json ile yayinlanan varsayilan HS256 secret'i herkes bilir; bununla imzalanan
    // token (admin dahil) gecerli sayilacagi icin yalnizca Development/Testing'de kabul edilir.
    private const string DefaultSecretPrefix = "CHANGE_ME";

    public JwtService(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        _issuer = configuration["Jwt:Issuer"] ?? "karar-api";
        _audience = configuration["Jwt:Audience"] ?? "karar-mobile";

        var privateKeyPem = configuration["Jwt:PrivateKeyPem"];
        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            var currentKeyId = configuration["Jwt:KeyId"] ?? "primary";
            var privateRsa = RSA.Create();
            privateRsa.ImportFromPem(privateKeyPem);
            _signingKey = new RsaSecurityKey(privateRsa) { KeyId = currentKeyId };
            _validationKeys = BuildRsaValidationKeys(configuration, privateRsa, currentKeyId);
            _algorithm = SecurityAlgorithms.RsaSha256;
        }
        else
        {
            var secret = configuration["Jwt:Secret"]
                ?? throw new InvalidOperationException("Jwt:Secret config eksik.");
            if (secret.Length < 32)
                throw new InvalidOperationException("Jwt:Secret en az 32 karakter olmali.");
            var allowsDefaultSecret = environment is not null &&
                (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
            if (!allowsDefaultSecret && secret.StartsWith(DefaultSecretPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Jwt:Secret varsayilan degerde; Development/Testing disinda Jwt:Secret veya Jwt:PrivateKeyPem tanimlanmalidir.");

            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
            {
                KeyId = configuration["Jwt:KeyId"] ?? "dev",
            };
            _validationKeys = [_signingKey];
            _algorithm = SecurityAlgorithms.HmacSha256;
        }
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateAdminToken(string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // JWT "exp" saniye hassasiyetindedir; yanittaki expiresAt token'daki exp ile birebir ayni olsun.
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.Add(AdminTokenLifetime).ToUnixTimeSeconds());

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, _algorithm)
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateAccessToken(Guid userId, string username)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_accessTokenLifetime),
            signingCredentials: new SigningCredentials(_signingKey, _algorithm)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            // MapInboundClaims=false: claim'ler token'a yazildigi adla gelir ("sub", "email",
            // "unique_name"). Varsayilan esleme bunlari ClaimTypes.NameIdentifier/Email/Name'e
            // cevirdigi icin JwtRegisteredClaimNames ile yapilan okumalar null donuyordu
            // (admin JWT'leri bu yuzden hep reddediliyordu).
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = _validationKeys,
                ValidAlgorithms = [_algorithm],
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// Returns bytes suitable for HMAC signing (e.g., for export integrity chain).
    /// In HS256 mode returns the symmetric key bytes; in RS256 mode derives a 32-byte
    /// key from the private exponent so exports remain server-verifiable.
    public byte[] GetHmacSigningBytes()
    {
        if (_signingKey is SymmetricSecurityKey sym)
            return sym.Key;

        if (_signingKey is RsaSecurityKey rsaKey)
        {
            var priv = rsaKey.Rsa.ExportParameters(includePrivateParameters: true);
            return SHA256.HashData(priv.D ?? priv.Modulus ?? []);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(_issuer + _audience));
    }

    public object? GetJwks()
    {
        var rsaKeys = _validationKeys.OfType<RsaSecurityKey>().ToArray();
        if (rsaKeys.Length == 0) return null;

        return new
        {
            keys = rsaKeys.Select(rsaKey =>
            {
                var parameters = rsaKey.Rsa.ExportParameters(false);
                return new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = rsaKey.KeyId ?? "primary",
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                };
            }).ToArray()
        };
    }

    private static IReadOnlyList<SecurityKey> BuildRsaValidationKeys(
        IConfiguration configuration,
        RSA privateRsa,
        string currentKeyId)
    {
        var keys = new List<SecurityKey>();
        var currentPublicKeyPem = configuration["Jwt:PublicKeyPem"];
        var currentPublicRsa = string.IsNullOrWhiteSpace(currentPublicKeyPem)
            ? RSA.Create(privateRsa.ExportParameters(false))
            : ImportPublicKey(currentPublicKeyPem);
        keys.Add(new RsaSecurityKey(currentPublicRsa) { KeyId = currentKeyId });

        var previousPublicKeyPem = configuration["Jwt:PreviousPublicKeyPem"];
        if (!string.IsNullOrWhiteSpace(previousPublicKeyPem))
        {
            keys.Add(new RsaSecurityKey(ImportPublicKey(previousPublicKeyPem))
            {
                KeyId = configuration["Jwt:PreviousKeyId"] ?? "previous",
            });
        }

        return keys;
    }

    private static RSA ImportPublicKey(string publicKeyPem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return rsa;
    }
}
