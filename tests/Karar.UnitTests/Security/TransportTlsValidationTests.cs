using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Karar.Api.Data;
using Karar.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Karar.UnitTests.Security;

/// Production Postgres (Neon) ve Redis (Upstash) bağlantıları Render'dan internete çıkar.
/// Sunucu sertifikası doğrulanmazsa araya giren biri (DNS hijack, transit) kendi
/// self-signed sertifikasıyla TLS oturumunu açar ve parolayı düz metin olarak alır.
/// Bu testler loopback üzerinde self-signed sertifikalı sahte bir sunucuyla bu
/// senaryoyu gerçek Db / RedisService sınıflarına karşı birebir dener.
public sealed class TransportTlsValidationTests
{
    private const string DbPassword = "db-password-must-not-leak";
    private const string RedisPassword = "redis-password-must-not-leak";
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData("postgresql://karar:pw@ep-x.eu-central-1.aws.neon.tech/neondb")]
    [InlineData("postgresql://karar:pw@ep-x.eu-central-1.aws.neon.tech/neondb?sslmode=require")]
    [InlineData("postgres://karar:pw@ep-x.eu-central-1.aws.neon.tech:5432/neondb?sslmode=require&channel_binding=require")]
    [InlineData("postgresql://karar:pw@ep-x.eu-central-1.aws.neon.tech/neondb?sslmode=verify-full")]
    public void ConvertToKeyValue_ProviderUri_VerifiesServerCertificateAndHostName(string uri)
    {
        var builder = new NpgsqlConnectionStringBuilder(Db.ConvertToKeyValue(uri));

        builder.SslMode.Should().Be(SslMode.VerifyFull,
            "libpq 'require' only encrypts; it never authenticates the server");
        builder.TrustServerCertificate.Should().BeFalse();
    }

    [Theory]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("prefer", SslMode.Prefer)]
    public void ConvertToKeyValue_ExplicitNonRequireSslMode_IsHonoured(string sslmode, SslMode expected)
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Db.ConvertToKeyValue($"postgresql://karar:pw@db.internal:5432/karar?sslmode={sslmode}"));

        builder.SslMode.Should().Be(expected);
        builder.TrustServerCertificate.Should().BeFalse();
    }

    [Fact]
    public void ConvertToKeyValue_KeyValueConnectionString_IsPassedThroughUnchanged()
    {
        const string keyValue = "Host=postgres;Port=5432;Database=karar;Username=karar;Password=x";

        Db.ConvertToKeyValue(keyValue).Should().Be(keyValue);
    }

    [Fact]
    public async Task Db_UriConnection_RejectsSelfSignedServer_AndNeverSendsPassword()
    {
        using var certificate = CreateSelfSignedCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource(ServerTimeout);
            var server = RunFakePostgresAsync(listener, certificate, cts.Token);

            var config = Configuration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = $"postgresql://karar:{DbPassword}@127.0.0.1:{port}/karar",
            });
            using var db = new Db(config);

            var connect = async () => { await using var _ = await db.OpenConnectionAsync(); };
            await connect.Should().ThrowAsync<Exception>();

            var outcome = await server;
            outcome.CapturedSecret.Should().BeNull(
                "a forged TLS endpoint must never receive the database password");
            outcome.ReceivedApplicationData.Should().BeFalse(
                "the client must abort the session when the certificate does not chain to a trusted CA");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RedisService_Ssl_RejectsSelfSignedServer_AndNeverSendsPassword()
    {
        using var certificate = CreateSelfSignedCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource(ServerTimeout);
            var server = RunFakeRedisAsync(listener, certificate, cts.Token);

            var config = Configuration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = $"127.0.0.1:{port},abortConnect=false,connectTimeout=3000,connectRetry=1",
                ["Redis:Password"] = RedisPassword,
                ["Redis:Ssl"] = "true",
            });

            using (await Task.Run(() => new RedisService(config, NullLogger<RedisService>.Instance)))
            {
                var outcome = await server;
                outcome.CapturedSecret.Should().BeNull(
                    "a forged TLS endpoint must never receive the Redis AUTH password");
                outcome.ReceivedApplicationData.Should().BeFalse(
                    "the client must abort the session when the certificate does not chain to a trusted CA");
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    // TLS 1.3'te sunucu el sıkışmayı istemci sertifikayı doğrulamadan bitirir; bu yüzden
    // ölçüt, istemcinin sahte oturum üzerinden herhangi bir uygulama verisi gönderip göndermediğidir.
    private sealed record MitmOutcome(bool ReceivedApplicationData, string? CapturedSecret);

    // Postgres wire protocol: SSLRequest → 'S' → TLS → StartupMessage →
    // AuthenticationCleartextPassword → PasswordMessage ('p').
    private static async Task<MitmOutcome> RunFakePostgresAsync(
        TcpListener listener, X509Certificate2 certificate, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var network = client.GetStream();

        var sslRequest = new byte[8];
        await network.ReadExactlyAsync(sslRequest, ct);
        await network.WriteAsync(new[] { (byte)'S' }, ct);
        await network.FlushAsync(ct);

        await using var tls = new SslStream(network, leaveInnerStreamOpen: false);
        if (!await TryAuthenticateAsServerAsync(tls, certificate, ct))
            return new MitmOutcome(false, null);

        try
        {
            await ReadLengthPrefixedAsync(tls, ct); // StartupMessage (kullanıcı adı, veritabanı)
        }
        catch (Exception ex) when (IsSessionAbort(ex))
        {
            return new MitmOutcome(false, null);
        }

        try
        {
            await tls.WriteAsync(new byte[] { (byte)'R', 0, 0, 0, 8, 0, 0, 0, 3 }, ct);
            await tls.FlushAsync(ct);

            var type = new byte[1];
            await tls.ReadExactlyAsync(type, ct);
            var body = await ReadLengthPrefixedAsync(tls, ct);
            var password = Encoding.UTF8.GetString(body).TrimEnd('\0');
            return new MitmOutcome(true, type[0] == (byte)'p' ? password : null);
        }
        catch (Exception ex) when (IsSessionAbort(ex))
        {
            return new MitmOutcome(true, null);
        }
    }

    private static async Task<MitmOutcome> RunFakeRedisAsync(
        TcpListener listener, X509Certificate2 certificate, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        if (!await TryAuthenticateAsServerAsync(tls, certificate, ct))
            return new MitmOutcome(false, null);

        var received = new StringBuilder();
        var buffer = new byte[4096];
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (!received.ToString().Contains(RedisPassword, StringComparison.Ordinal))
            {
                var read = await tls.ReadAsync(buffer, readCts.Token);
                if (read == 0) break;
                received.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }
        }
        catch (Exception ex) when (IsSessionAbort(ex))
        {
        }

        return new MitmOutcome(received.Length > 0,
            received.ToString().Contains(RedisPassword, StringComparison.Ordinal) ? RedisPassword : null);
    }

    private static async Task<bool> TryAuthenticateAsServerAsync(
        SslStream tls, X509Certificate2 certificate, CancellationToken ct)
    {
        try
        {
            await tls.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = certificate }, ct);
            return true;
        }
        catch (Exception ex) when (IsSessionAbort(ex))
        {
            return false;
        }
    }

    private static bool IsSessionAbort(Exception ex) =>
        ex is AuthenticationException or IOException or Win32Exception or OperationCanceledException;

    private static async Task<byte[]> ReadLengthPrefixedAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        var body = new byte[length - 4];
        await stream.ReadExactlyAsync(body, ct);
        return body;
    }

    // Saldırganın elinde ancak self-signed bir sertifika olur; ad (127.0.0.1) eşleşir,
    // tek eksik güvenilir bir CA zinciridir.
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, critical: false));

        using var ephemeral = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Windows SChannel geçici anahtarla sunucu olamaz; PFX üzerinden yeniden yüklenir.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
