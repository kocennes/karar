using System.Security.Cryptography;

namespace Karar.Api.Services;

// RFC 6238 TOTP implementasyonu — harici paket gerektirmez.
// Google Authenticator ile uyumlu: SHA-1, 30 saniyelik adım, 6 haneli kod.
public static class TotpService
{
    private const int Step = 30;
    private const int Digits = 6;

    // base32Secret: Google Authenticator'dan alınan Base32 secret (boşluk/tire tolere edilir).
    // tolerance: kaç adım ileri/geri kontrol edilsin (1 = ±30s, saati kayma payı).
    public static bool Validate(string base32Secret, string code, int tolerance = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || !int.TryParse(code, out var givenCode))
            return false;

        byte[] key;
        try { key = Base32Decode(base32Secret); }
        catch { return false; }

        if (key.Length == 0) return false;

        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Step;

        for (var i = -tolerance; i <= tolerance; i++)
        {
            if (Generate(key, counter + i) == givenCode)
                return true;
        }

        return false;
    }

    private static int Generate(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        var hmac = HMACSHA1.HashData(key, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binary = ((hmac[offset] & 0x7F) << 24)
            | (hmac[offset + 1] << 16)
            | (hmac[offset + 2] << 8)
            | hmac[offset + 3];

        return binary % (int)Math.Pow(10, Digits);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.ToUpperInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .TrimEnd('=');

        var bits = 0;
        var bitCount = 0;
        var result = new List<byte>(input.Length * 5 / 8);

        foreach (var c in input)
        {
            var value = alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"Geçersiz Base32 karakter: {c}");
            bits = (bits << 5) | value;
            bitCount += 5;
            if (bitCount >= 8)
            {
                result.Add((byte)(bits >> (bitCount - 8)));
                bitCount -= 8;
            }
        }

        return [.. result];
    }
}
