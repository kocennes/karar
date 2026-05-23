using System.Net;
using System.Text.RegularExpressions;

namespace Karar.Api.Common;

public static class SensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";

    private static readonly Regex JsonKeyValuePattern = new(
        @"(?i)""(access[_-]?token|accesstoken|refresh[_-]?token|refreshtoken|device[_-]?id|deviceid|device[_-]?token|devicetoken|fcm[_-]?token|fcmtoken|authorization|password|otp|totp[_-]?code|totpcode|backup[_-]?code|backupcode|email)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled);

    private static readonly Regex PlainKeyValuePattern = new(
        @"(?i)\b(access[_-]?token|accesstoken|refresh[_-]?token|refreshtoken|device[_-]?id|deviceid|device[_-]?token|devicetoken|fcm[_-]?token|fcmtoken|authorization|password|otp|totp[_-]?code|totpcode|backup[_-]?code|backupcode|email)\b\s*([:=])\s*([^&\s,""}]+)",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex Ipv4Pattern = new(
        @"\b(?<a>\d{1,3})\.(?<b>\d{1,3})\.(?<c>\d{1,3})\.(?<d>\d{1,3})\b",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var redacted = BearerPattern.Replace(value, $"Bearer {Redacted}");
        redacted = JsonKeyValuePattern.Replace(redacted, match =>
        {
            var key = match.Groups[1].Value;
            return $"\"{key}\":\"{Redacted}\"";
        });
        redacted = PlainKeyValuePattern.Replace(redacted, match =>
        {
            var key = match.Groups[1].Value;
            var separator = match.Groups[2].Value;
            return $"{key}{separator}{Redacted}";
        });
        redacted = EmailPattern.Replace(redacted, Redacted);
        redacted = Ipv4Pattern.Replace(redacted, match => MaskIpv4(match.Value));
        return redacted;
    }

    public static string RedactHeader(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return IsSensitiveKey(name)
            ? Redacted
            : Redact(value);
    }

    public static string RedactIp(IPAddress? address)
    {
        if (address is null) return "unknown";

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return MaskIpv4(address.ToString());

        var parts = address.ToString().Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? "[IPv6]" : string.Join(':', parts.Take(2)) + "::/32";
    }

    private static bool IsSensitiveKey(string name)
    {
        var normalized = name.Replace("-", string.Empty).Replace("_", string.Empty);
        return normalized.Equals("authorization", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("xdevicetoken", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("cookie", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("otp", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("email", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("deviceid", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskIpv4(string value)
    {
        return IPAddress.TryParse(value, out var address)
               && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? string.Join('.', address.ToString().Split('.').Take(3)) + ".0/24"
            : value;
    }
}
