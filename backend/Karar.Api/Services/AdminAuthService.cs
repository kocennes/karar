using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Karar.Api.Contracts;

namespace Karar.Api.Services;

public sealed class AdminAuthService
{
    private readonly string _email;
    private readonly string _password;
    private readonly string? _passwordHash;
    private readonly HashSet<IPAddress> _ipAllowlist;
    private readonly JwtService _jwt;

    public AdminAuthService(IConfiguration configuration, IWebHostEnvironment env, JwtService jwt)
    {
        _jwt = jwt;

        // Yayınlanmış varsayılan şifre yalnızca Development/Testing'de kabul edilir; Staging dahil
        // internete açılabilecek her ortamda gerçek bir şifre/hash zorunludur.
        var allowsDevDefaults = env.IsDevelopment() || env.IsEnvironment("Testing");

        _email = configuration["Admin:Email"] ?? "admin@karar.local";
        _password = configuration["Admin:Password"] ?? "dev-admin-password";
        _passwordHash = configuration["Admin:PasswordHash"];

        if (!allowsDevDefaults && _passwordHash is null or { Length: 0 } &&
            (_password == "dev-admin-password" || string.IsNullOrWhiteSpace(_password)))
            throw new InvalidOperationException(
                "Admin:Password veya Admin:PasswordHash Development/Testing disinda zorunludur.");

        var allowlistRaw = configuration["Admin:IpAllowlist"] ?? "";
        _ipAllowlist = ParseIpAllowlist(allowlistRaw);
    }

    public bool ValidateCredentials(string email, string password)
    {
        if (!string.Equals(email, _email, StringComparison.OrdinalIgnoreCase))
            return false;

        return _passwordHash is { Length: > 0 }
            ? PasswordService.Verify(password, _passwordHash)
            : password == _password;
    }

    // Her girişte yeni, imzalı ve süreli bir admin JWT'si üretir. Statik/paylaşılan token yoktur.
    public AdminLoginResponse IssueToken()
    {
        var (token, expiresAt) = _jwt.GenerateAdminToken(_email);
        return new AdminLoginResponse(token, expiresAt);
    }

    public string? TryGetAdminEmail(HttpRequest request)
    {
        if (!IsIpAllowed(request.HttpContext)) return null;
        var token = GetToken(request);
        return ValidateAdminToken(token);
    }

    public bool IsIpAllowed(HttpContext context)
    {
        if (_ipAllowlist.Count == 0) return true;
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && _ipAllowlist.Contains(remoteIp.MapToIPv4());
    }

    private string? ValidateAdminToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        // Yalnızca imzası, süresi, issuer/audience'ı geçerli ve role=admin olan JWT kabul edilir.
        var principal = _jwt.ValidateAccessToken(token);
        if (principal is null) return null;
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (role != "admin") return null;
        return principal.FindFirstValue(JwtRegisteredClaimNames.Email)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    }

    private static string? GetToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Admin-Token", out var adminToken))
            return adminToken.ToString();

        if (!request.Headers.TryGetValue("Authorization", out var authValues))
            return null;

        var authorization = authValues.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private static HashSet<IPAddress> ParseIpAllowlist(string raw)
    {
        var set = new HashSet<IPAddress>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(part, out var ip))
                set.Add(ip.MapToIPv4());
        }
        return set;
    }
}
