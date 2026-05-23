using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Karar.Api.Services;

public static class PasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 4;
    private const int MemorySize = 65536; // 64 MB
    private const int DegreeOfParallelism = 2;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(password, salt);
        var result = new byte[SaltSize + HashSize];
        salt.CopyTo(result, 0);
        hash.CopyTo(result, SaltSize);
        return Convert.ToBase64String(result);
    }

    public static bool Verify(string password, string storedHash)
    {
        try
        {
            var data = Convert.FromBase64String(storedHash);
            if (data.Length != SaltSize + HashSize) return false;
            var salt = data[..SaltSize];
            var expectedHash = data[SaltSize..];
            var actualHash = ComputeHash(password, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySize,
            DegreeOfParallelism = DegreeOfParallelism,
        };
        return argon2.GetBytes(HashSize);
    }

    public static string GenerateOtp()
    {
        var number = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return number.ToString("D6");
    }

    public static string HashOtp(string otp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
