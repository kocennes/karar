using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;

namespace Karar.Api.Services;

public sealed class AdminAuthService
{
    private readonly string _email;
    private readonly string _password;
    private readonly string? _passwordHash;
    private readonly string? _plainToken;
    private readonly HashSet<IPAddress> _ipAllowlist;
    private readonly JwtService _jwt;

    public AdminAuthService(IConfiguration configuration, IWebHostEnvironment env, JwtService jwt)
    {
        _jwt = jwt;

        var isProduction = env.IsProduction();

        _email = configuration["Admin:Email"] ?? "admin@karar.local";
        _password = configuration["Admin:Password"] ?? "dev-admin-password";
        _passwordHash = configuration["Admin:PasswordHash"];
        _plainToken = configuration["Admin:Token"];

        if (isProduction && _passwordHash is null or { Length: 0 } &&
            (_password == "dev-admin-password" || string.IsNullOrWhiteSpace(_password)))
            throw new InvalidOperationException(
                "Admin:Password veya Admin:PasswordHash production ortaminda zorunludur.");

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

    public string IssueToken() => _plainToken ?? _jwt.GenerateAdminToken(_email);

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

        // Plain token öncelikli — JWT validasyon sorunlarını bypass eder
        if (_plainToken is { Length: > 0 } && token == _plainToken)
            return _email;

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
