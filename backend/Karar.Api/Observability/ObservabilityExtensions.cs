using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Karar.Api.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddKararObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<SloOptions>(configuration.GetSection("Slo"));
        services.AddSingleton<SloMetrics>();
        services.AddSingleton<BurnRateAlertState>();
        services.AddHttpClient("slo-alerts");
        services.AddHostedService<BurnRateAlertWorker>();

        if (!IsTelemetryEnabled(configuration))
            return services;

        var otlpEndpoint = configuration["Observability:OtlpEndpoint"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: KararTelemetry.ServiceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(KararTelemetry.ActivitySourceName)
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("deployment.environment", environment.EnvironmentName);
                            activity.SetTag("http.route", request.HttpContext.GetEndpoint()
                                ?.Metadata.GetMetadata<RouteEndpoint>()
                                ?.RoutePattern.RawText);
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    });

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(KararTelemetry.MeterName)
                    .AddMeter("Karar.Api.Business")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }

    public static IApplicationBuilder UseKararSloMetrics(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var metrics = ctx.RequestServices.GetRequiredService<SloMetrics>();
            var started = TimeProvider.System.GetTimestamp();
            try
            {
                await next();
            }
            finally
            {
                var elapsed = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
                var route = ctx.GetEndpoint()
                    ?.Metadata.GetMetadata<RouteEndpoint>()
                    ?.RoutePattern.RawText
                    ?? SanitizePath(ctx.Request.Path.Value);

                metrics.RecordApiRequest(route, ctx.Request.Method, ctx.Response.StatusCode, elapsed);
                Activity.Current?.SetTag("karar.slo.route", route);
                Activity.Current?.SetTag("karar.slo.status_class", KararTelemetry.StatusClass(ctx.Response.StatusCode));
            }
        });
    }

    private static bool IsTelemetryEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("Observability:Enabled") ||
        !string.IsNullOrWhiteSpace(configuration["Observability:OtlpEndpoint"]) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    private static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Guid.TryParse(segment, out _) ? "{id}" : segment);

        return "/" + string.Join('/', segments);
    }
}
