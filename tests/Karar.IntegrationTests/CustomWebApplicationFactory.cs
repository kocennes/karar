using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Karar.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=karar_test;Username=karar;Password=karar_dev_password",
                ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false",
                ["Admin:Token"] = "test-admin-token",
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
}
