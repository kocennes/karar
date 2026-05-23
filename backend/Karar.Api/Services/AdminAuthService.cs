using System.Net;

namespace Karar.Api.Services;

public sealed class AdminAuthService
{
    private readonly string _staticToken;
    private readonly string? _previousStaticToken;
    private readonly string _email;
    private readonly string _password;
    private readonly string? _passwordHash;
    private readonly string? _totpSecret;
    private readonly HashSet<IPAddress> _ipAllowlist;

    public AdminAuthService(IConfiguration configuration, IWebHostEnvironment env)
    {
        var isProduction = env.IsProduction();

        var token = configuration["Admin:Token"];
        if (isProduction && string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Admin:Token Secret Manager'dan yüklenmeli. " +
                "Cloud Run servisini --set-secrets=Admin__Token=admin-token:latest ile yapılandırın.");

        _staticToken = token ?? "dev-admin-token";
        _previousStaticToken = configuration["Admin:PreviousToken"];
        _email = configuration["Admin:Email"] ?? "admin@karar.local";
        _password = configuration["Admin:Password"] ?? "dev-admin-password";
        _passwordHash = configuration["Admin:PasswordHash"];

        if (isProduction && _passwordHash is null or { Length: 0 } &&
            (_password == "dev-admin-password" || string.IsNullOrWhiteSpace(_password)))
            throw new InvalidOperationException(
                "Admin:PasswordHash Secret Manager'dan yüklenmeli. " +
                "Cloud Run servisini --set-secrets=Admin__PasswordHash=admin-password-hash:latest ile yapılandırın.");

        // Admin:TotpSecret → Base32 secret (Google Authenticator ile taranır).
        // Boşsa dev ortamında TOTP doğrulaması atlanır, prod'da zorunlu.
        _totpSecret = configuration["Admin:TotpSecret"];
        if (isProduction && string.IsNullOrWhiteSpace(_totpSecret))
            throw new InvalidOperationException(
                "Admin:TotpSecret Secret Manager'dan yüklenmeli. " +
                "Cloud Run servisini --set-secrets=Admin__TotpSecret=admin-totp-secret:latest ile yapılandırın.");

        // Admin:IpAllowlist → virgülle ayrılmış IP adresleri (boşsa kısıtlama yok).
        var allowlistRaw = configuration["Admin:IpAllowlist"] ?? "";
        _ipAllowlist = ParseIpAllowlist(allowlistRaw);
    }

    public string? TryGetAdminEmail(HttpRequest request)
    {
        if (!IsIpAllowed(request.HttpContext)) return null;
        var token = GetToken(request);
        return IsValidToken(token) ? _email : null;
    }

    // Giriş doğrulama: e-posta + şifre + TOTP (secret tanımlıysa zorunlu).
    public bool ValidateLogin(string email, string password, string totpCode)
    {
        if (!string.Equals(email, _email, StringComparison.OrdinalIgnoreCase))
            return false;

        var passwordOk = _passwordHash is { Length: > 0 }
            ? PasswordService.Verify(password, _passwordHash)
            : password == _password;

        if (!passwordOk) return false;

        // Prod: TotpSecret tanımlıysa RFC 6238 doğrula; dev: secret yoksa atla.
        if (!string.IsNullOrWhiteSpace(_totpSecret))
            return TotpService.Validate(_totpSecret, totpCode);

        return true;
    }

    public string IssueToken() => _staticToken;

    public bool IsIpAllowed(HttpContext context)
    {
        if (_ipAllowlist.Count == 0) return true;
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && _ipAllowlist.Contains(remoteIp.MapToIPv4());
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

    private bool IsValidToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        return token == _staticToken ||
               (!string.IsNullOrWhiteSpace(_previousStaticToken) && token == _previousStaticToken);
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
