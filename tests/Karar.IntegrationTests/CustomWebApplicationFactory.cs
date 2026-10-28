using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Karar.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // TestServer leaves RemoteIpAddress null, so every request of the suite lands in the same
        // "unknown" partition of the API's per-IP limiters (global 100/min, admin API 60/5 min in Redis).
        // With a reachable Redis the suite runs in seconds and exceeds them (429 instead of the asserted
        // status); it only passed before because Redis was unreachable (slow, fail-open). Each request
        // gets its own address from the 198.18.0.0/15 benchmarking range instead.
        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, PerRequestClientIpStartupFilter>());

        // Scoped servis doğrulamasını test ortamında devre dışı bırak.
        // RequestDevice scoped kayıtlıdır ve root provider'dan çözülmez;
        // bu kontrol production'da geçersizdir, test ortamında yanlış pozitif verir.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Short timeouts so tests fail fast instead of hanging:
                // - Postgres: Timeout=3 (connection), Command Timeout=3 (query) instead of 15s defaults
                // - Redis: connectTimeout=500ms instead of the 5000ms default that can block for 30s
                // KARAR_TEST_POSTGRES / KARAR_TEST_REDIS point the suite at another stack (e.g. a local
                // Postgres on a non-default port); this in-memory source would otherwise override env vars.
                ["ConnectionStrings:Postgres"] = Environment.GetEnvironmentVariable("KARAR_TEST_POSTGRES")
                    ?? "Host=localhost;Port=5432;Database=karar_test;Username=karar;Password=karar_dev_password;Timeout=2;Command Timeout=2",
                ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("KARAR_TEST_REDIS")
                    ?? "localhost:6379,abortConnect=false,connectTimeout=500,syncTimeout=200",
                ["Admin:Email"] = "admin@test.local",
                ["Admin:Password"] = "test-admin-password",
                ["Admin:TotpSecret"] = "",
                ["Jwt:Secret"] = "test-secret-minimum-32-characters-long-here",
                ["Jwt:Issuer"] = "karar-api",
                ["Jwt:Audience"] = "karar-mobile",
                ["Firebase:ServiceAccountJson"] = "",
                ["Notifications:RamadanMode"] = "false",
                ["Testing:DisableHostedServices"] = "true",
                ["Web:BaseUrl"] = "http://localhost",
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        try
        {
            return base.CreateHost(builder);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Integration test sunucusu başlatılamadı: {ex.Message}\n" +
                "DB gerektiren testler için: docker-compose up -d", ex);
        }
    }

    private sealed class PerRequestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= RandomBenchmarkAddress();
                return nextMiddleware(context);
            });
            next(app);
        };

        // 198.18.0.0/15 (RFC 2544): never routed, never a real client; random so that re-runs within the
        // Redis windows (device registration budget per IP and per /24) do not reuse the same addresses.
        private static IPAddress RandomBenchmarkAddress()
        {
            var n = Random.Shared.Next(0, 1 << 17);
            return new IPAddress([198, (byte)(18 + (n >> 16)), (byte)((n >> 8) & 0xFF), (byte)(n & 0xFF)]);
        }
    }
}
