using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Karar.Api.Common;
using Karar.Api.Common.Middleware;
using Karar.Api.Contracts;
using Karar.Api.Data;
using Karar.Api.Models;
using Karar.Api.Observability;
using Karar.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

// Bootstrap logger: paket yüklenmeden önce çöken hataları yakalar.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// Faz 5: GCP Secret Manager entegrasyonu (Production'da)
if (builder.Environment.IsProduction())
{
    var projectId = builder.Configuration["GCP_PROJECT_ID"];
    if (!string.IsNullOrEmpty(projectId))
    {
        builder.Configuration.AddGcpSecrets(projectId);
    }
}

// Production'da stdout'a CLEF JSON (Cloud Logging tarafından otomatik okunur).
// Development'ta düz okunabilir konsol çıktısı.
builder.Host.UseSerilog((ctx, services, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("service", "karar-api")
       .WriteTo.Console(
           ctx.HostingEnvironment.IsProduction()
               ? (Serilog.Formatting.ITextFormatter)new CompactJsonFormatter()
               : new Serilog.Formatting.Display.MessageTemplateTextFormatter(
                   "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<DeviceTokenService>();
builder.Services.AddSingleton<ContentModerationService>();
builder.Services.AddSingleton<PerspectiveApiService>();
builder.Services.AddTransient<SsrfProtectionHandler>();
builder.Services.AddHttpClient("perspective", c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
})
.AddHttpMessageHandler<SsrfProtectionHandler>()
.AddStandardResilienceHandler();

var storageProvider = builder.Configuration["Storage:Provider"]?.ToLowerInvariant();
if (storageProvider == "supabase")
{
    builder.Services.AddSingleton<IStorageService, SupabaseStorageService>();
}
else
{
    builder.Services.AddSingleton<IStorageService, CloudStorageService>();
}
builder.Services.AddSingleton<SafeSearchService>();
builder.Services.AddSingleton<ImageProcessorService>();
builder.Services.AddSingleton<ShareImageService>();
builder.Services.AddHttpClient("vision", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<SsrfProtectionHandler>()
.AddStandardResilienceHandler();

builder.Services.AddHttpClient("play-integrity", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler();
builder.Services.AddHttpClient("resend", c =>
{
    c.BaseAddress = new Uri("https://api.resend.com/");
    c.Timeout = TimeSpan.FromSeconds(15);
    var apiKey = builder.Configuration["Resend:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});
builder.Services.AddHttpClient("brevo", c =>
{
    c.BaseAddress = new Uri("https://api.brevo.com/v3/");
    c.Timeout = TimeSpan.FromSeconds(15);
    var apiKey = builder.Configuration["Brevo:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        c.DefaultRequestHeaders.Add("api-key", apiKey);
});
builder.Services.AddSingleton<PlayIntegrityService>();
var hostedServicesEnabled = !builder.Environment.IsEnvironment("Testing")
    && !builder.Configuration.GetValue("Testing:DisableHostedServices", false);
if (hostedServicesEnabled)
{
    builder.Services.AddHostedService<ImageModerationWorker>();
}
builder.Services.AddSingleton<ReportThresholdService>();
builder.Services.AddSingleton<ReporterReputationService>();
builder.Services.AddSingleton<ReportAbuseProtectionService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<NotificationRateLimiter>();
builder.Services.AddSingleton<NotificationPreferenceRouter>();
builder.Services.AddSingleton<NotificationDecisionService>();
builder.Services.AddSingleton<BruteForceService>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<FirebaseAuthService>();
builder.Services.AddSingleton<IFcmSender, FirebaseFcmSender>();
builder.Services.AddSingleton<SseConnectionManager>();
builder.Services.AddSingleton<CommentNotificationBatcher>();
if (hostedServicesEnabled)
{
    builder.Services.AddHostedService<TrendScoreUpdater>();
    builder.Services.AddHostedService<NotificationDispatcher>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CommentNotificationBatcher>());
    builder.Services.AddHostedService<VerdictReminderJob>();
    builder.Services.AddHostedService<ViralNotificationJob>();
    builder.Services.AddHostedService<DeferredNotificationFlushJob>();
    builder.Services.AddHostedService<DataRetentionService>();
    builder.Services.AddHostedService<PoliticalNarrativeClusterJob>();
    builder.Services.AddHostedService<BrigadeCoordinatedDetectorJob>();
    builder.Services.AddHostedService<PostDistributionJob>();
    builder.Services.AddHostedService<AuditLogExportJob>();
}
builder.Services.AddSingleton<CategoryThrottleService>();
builder.Services.AddSingleton<AffinityService>();
builder.Services.AddSingleton<GeoService>();
builder.Services.AddSingleton<DeviceTrustService>();
builder.Services.AddSingleton<AppAttestService>();
builder.Services.AddSingleton<FirebaseAppCheckService>();
builder.Services.AddSingleton<AppAttestationService>();
builder.Services.AddSingleton<ComplianceLogService>();
builder.Services.AddSingleton<BusinessMetricsService>();
builder.Services.AddKararObservability(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<SubnetBanService>();
builder.Services.AddSingleton<VoteBrigadeGuard>();
builder.Services.AddScoped<RequestDevice>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global: 100 istek / dakika / IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );

    // SSE: IP baÅŸÄ±na eÅŸ zamanlÄ± baÄŸlantÄ± sÄ±nÄ±rÄ± â€" uzun yaÅŸayan baÄŸlantÄ±lar iÃ§in
    options.AddPolicy("sse", context =>
        RateLimitPartition.GetConcurrencyLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 10,
                QueueLimit = 0
            }
        )
    );

    // Auth fallback: 60 istek / dakika / IP. Redis brute-force korumasi ayrıca 10 deneme / 15 dakika uygular.
    options.AddPolicy("auth-strict", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );

    // Phase 5: Per-action production rate limits
    options.AddPolicy("post-create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }
        )
    );

    options.AddPolicy("vote", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );

    options.AddPolicy("growth-events", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );

    options.AddPolicy("admin-analytics-export", context =>
    {
        var partitionKey = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(partitionKey))
            partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        );
    });

    options.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = new { code = "RATE_LIMIT_EXCEEDED", message = "Ã‡ok fazla istek. LÃ¼tfen bekleyin." }
        });
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("flutter-app", policy =>
    {
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") || builder.Environment.IsStaging())
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            var configuredOrigins = builder.Configuration
                .GetSection("AllowedOrigins")
                .Get<string[]>();
            var origins = configuredOrigins?.Length > 0
                ? configuredOrigins
                : ["https://karar.app", "https://www.karar.app", "https://admin.karar.app", "https://admin-karar.vercel.app", "https://karar-admin.vercel.app", "https://karar-admin-git-main-enes-koc-karar-s-projects.vercel.app", "https://judge-app-karar.web.app", "https://judge-app-karar.firebaseapp.com"];
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddResponseCompression(opt =>
{
    opt.EnableForHttps = true;
    opt.Providers.Add<BrotliCompressionProvider>();
    opt.Providers.Add<GzipCompressionProvider>();
    opt.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});

var postgresCs = Karar.Api.Data.Db.ConvertToKeyValue(
    builder.Configuration.GetConnectionString("Postgres")!);
var redisCs = builder.Configuration.GetConnectionString("Redis");
var healthChecks = builder.Services.AddHealthChecks()
    .AddNpgSql(postgresCs, name: "postgres", tags: ["ready", "db"])
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck("firebase", () => FirebaseAdmin.FirebaseApp.DefaultInstance is not null
        ? HealthCheckResult.Healthy("Firebase Admin initialized")
        : HealthCheckResult.Degraded("Firebase Admin not initialized — push notifications unavailable"),
        tags: ["ready", "push"]);
if (!string.IsNullOrEmpty(redisCs))
{
    var redisPassword = builder.Configuration["Redis:Password"];
    var redisSsl = builder.Configuration.GetValue<bool>("Redis:Ssl");
    var redisFullCs = string.IsNullOrEmpty(redisPassword)
        ? redisCs
        : $"{redisCs},password={redisPassword},ssl={redisSsl.ToString().ToLower()}";
    healthChecks.AddRedis(redisFullCs, name: "redis", tags: ["ready", "cache"]);
}

// Migration check
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase) ||
    Environment.GetEnvironmentVariable("AUTO_MIGRATE") == "true")
{
    var connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));
    var migrationsPath = Path.Combine(AppContext.BaseDirectory, "migrations");
    await MigrationsRunner.RunAsync(connectionString, migrationsPath);

    // If explicitly called via CLI "migrate", exit. If via AUTO_MIGRATE, continue to start the app.
    if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase)) return;
}

var app = builder.Build();

// Global exception handler — logs full exception to console and returns JSON error body
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = new { code = "INTERNAL_ERROR", message = ex.Message, detail = ex.ToString() }
            });
        }
    }
});

// Cloud Run arkasÄ±ndaki gerÃ§ek client IP'sini al
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { new System.Net.IPNetwork(IPAddress.Parse("::ffff:0.0.0.0"), 96) }
});

app.UseResponseCompression();
app.UseCors("flutter-app");
app.UseMiddleware<RedactedRequestLoggingMiddleware>();
app.UseKararSloMetrics();

// Faz 5/6: Observability — X-Request-Id ve X-Response-Time ekle
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var requestId = Guid.NewGuid().ToString("N")[..12];
    ctx.Response.OnStarting(() =>
    {
        sw.Stop();
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Headers["X-Request-Id"] = requestId;
            ctx.Response.Headers["X-Response-Time"] = $"{sw.ElapsedMilliseconds}ms";
        }
        return Task.CompletedTask;
    });

    await next();
});

// Request body boyut limiti: 6 MB (gÃ¶rsel 5 MB + meta)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.ContentLength > 6_291_456)
    {
        ctx.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = new { code = "PAYLOAD_TOO_LARGE", message = "Ä°stek gÃ¶vdesi Ã§ok bÃ¼yÃ¼k. Maksimum 6 MB." }
        });
        return;
    }
    await next();
});

// Güvenlik header'ları
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    if (!ctx.Request.Path.StartsWithSegments("/api/v1/posts") ||
        !ctx.Request.Path.Value!.EndsWith("/share-image.png"))
    {
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'";
    }
    await next();
});

app.UseMiddleware<SubnetBanMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<DistributedRateLimitMiddleware>();
app.UseMiddleware<AppAttestationMiddleware>();
app.UseMiddleware<AdminSecurityMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("live"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = 200,
        [HealthStatus.Degraded]  = 200,
        [HealthStatus.Unhealthy] = 503
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = 200,
        [HealthStatus.Degraded]  = 200,
        [HealthStatus.Unhealthy] = 503
    }
});

app.MapGet("/health/version", (IConfiguration configuration, IHostEnvironment environment) =>
{
    var commitSha =
        configuration["Build:CommitSha"] ??
        Environment.GetEnvironmentVariable("GIT_COMMIT_SHA") ??
        Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ??
        Environment.GetEnvironmentVariable("VERCEL_GIT_COMMIT_SHA") ??
        Environment.GetEnvironmentVariable("SOURCE_VERSION") ??
        Environment.GetEnvironmentVariable("COMMIT_SHA") ??
        "unknown";

    return Results.Ok(new
    {
        service = "karar-api",
        environment = environment.EnvironmentName,
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        commitSha,
        deployedAt = configuration["Build:DeployedAt"] ?? Environment.GetEnvironmentVariable("DEPLOYED_AT") ?? "unknown"
    });
});

app.MapGet("/health/slo", (SloMetrics metrics) =>
{
    var snapshot = metrics.GetSnapshot();
    return Results.Ok(snapshot);
});

app.MapGet("/health/slo/alerts", (BurnRateAlertState alertState) =>
{
    var records = alertState.GetAll();
    var firing = records.Where(r => r.IsAlerting).ToArray();
    return Results.Ok(new
    {
        firing_count = firing.Length,
        firing,
        all = records
    });
});

app.MapGet("/robots.txt", (IConfiguration configuration) =>
{
    var webBaseUrl = GetWebBaseUrl(configuration);
    return Results.Content($"""
        User-agent: *
        Allow: /
        Sitemap: {webBaseUrl}/sitemap.xml
        """, "text/plain");
});

app.MapGet("/.well-known/apple-app-site-association", (IConfiguration configuration) =>
{
    // Faz 6: iOS Universal Links — bundle ID ve team ID buraya gelmeli.
    var teamId = configuration["Apple:TeamId"] ?? "TEAMID123";
    var bundleId = configuration["Apple:BundleId"] ?? "app.karar.mobile";

    return Results.Json(new
    {
        applinks = new
        {
            apps = Array.Empty<string>(),
            details = new[]
            {
                new
                {
                    appID = $"{teamId}.{bundleId}",
                    paths = new[] { "/posts/*", "/users/*" }
                }
            }
        }
    });
});

app.MapGet("/.well-known/assetlinks.json", (IConfiguration configuration) =>
{
    // Faz 6: Android App Links — SHA256 fingerprint buraya gelmeli.
    var packageName = configuration["Android:PackageName"] ?? "app.karar.mobile";
    var sha256 = configuration["Android:Sha256CertFingerprint"] ?? "00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00";

    return Results.Json(new[]
    {
        new
        {
            relation = new[] { "delegate_permission/common.handle_all_urls" },
            target = new
            {
                @namespace = "android_app",
                package_name = packageName,
                sha256_cert_fingerprints = new[] { sha256 }
            }
        }
    });
});

app.MapGet("/.well-known/jwks.json", (JwtService jwtService) =>
{
    var jwks = jwtService.GetJwks();
    return jwks is not null ? Results.Ok(jwks) : Results.NotFound();
});

app.MapGet("/api/v1/version", (IConfiguration configuration) =>
{
    return Results.Ok(new
    {
        minimumVersion = configuration["Version:MinimumVersion"] ?? "1.0.0",
        androidStoreUrl = configuration["Version:AndroidStoreUrl"] ?? "https://play.google.com/store/apps/details?id=app.karar",
        iosStoreUrl = configuration["Version:IosStoreUrl"] ?? "https://apps.apple.com/app/karar/id0000000000"
    });
});

app.MapGet("/sitemap.xml", async (HttpContext httpContext, Db db, IConfiguration configuration) =>
{
    var webBaseUrl = GetWebBaseUrl(configuration);
    await using var connection = await db.OpenConnectionAsync();

    // Get total count of active posts
    await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM posts WHERE status = 'active'", connection);
    var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
    var pageSize = 40000;
    var pageCount = (int)Math.Ceiling((double)totalCount / pageSize);

    httpContext.Response.ContentType = "application/xml; charset=utf-8";
    await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);

    await writer.WriteLineAsync("""<?xml version="1.0" encoding="UTF-8"?>""");
    await writer.WriteLineAsync("""<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

    // Main page sitemap
    await writer.WriteLineAsync($"""
      <sitemap>
        <loc>{webBaseUrl}/sitemap-main.xml</loc>
      </sitemap>
    """);

    for (int i = 1; i <= pageCount; i++)
    {
        await writer.WriteLineAsync($"""
          <sitemap>
            <loc>{webBaseUrl}/sitemap-posts-{i}.xml</loc>
          </sitemap>
        """);
    }

    await writer.WriteLineAsync("</sitemapindex>");
});

app.MapGet("/sitemap-main.xml", async (HttpContext httpContext, IConfiguration configuration) =>
{
    var webBaseUrl = GetWebBaseUrl(configuration);
    httpContext.Response.ContentType = "application/xml; charset=utf-8";
    await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);
    await writer.WriteLineAsync("""<?xml version="1.0" encoding="UTF-8"?>""");
    await writer.WriteLineAsync("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
    await writer.WriteLineAsync($"""
      <url>
        <loc>{webBaseUrl}/</loc>
        <changefreq>daily</changefreq>
        <priority>1.0</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/discover</loc>
        <changefreq>hourly</changefreq>
        <priority>0.9</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/legal/terms</loc>
        <changefreq>monthly</changefreq>
        <priority>0.5</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/legal/privacy</loc>
        <changefreq>monthly</changefreq>
        <priority>0.5</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/legal/community</loc>
        <changefreq>monthly</changefreq>
        <priority>0.5</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/legal/content-policy</loc>
        <changefreq>monthly</changefreq>
        <priority>0.5</priority>
      </url>
      <url>
        <loc>{webBaseUrl}/legal/contact</loc>
        <changefreq>monthly</changefreq>
        <priority>0.5</priority>
      </url>
    """);
    await writer.WriteLineAsync("</urlset>");
});

app.MapGet("/sitemap-posts-{page:int}.xml", async (int page, HttpContext httpContext, Db db, IConfiguration configuration) =>
{
    var pageSize = 40000;
    var offset = (page - 1) * pageSize;
    var webBaseUrl = GetWebBaseUrl(configuration);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT COALESCE(slug, id::text), updated_at
        FROM posts
        WHERE status = 'active'
        ORDER BY updated_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("limit", pageSize);
    command.Parameters.AddWithValue("offset", offset);

    httpContext.Response.ContentType = "application/xml; charset=utf-8";
    await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);

    await writer.WriteLineAsync("""<?xml version="1.0" encoding="UTF-8"?>""");
    await writer.WriteLineAsync("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var postSlug = reader.GetString(0);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(1);
        await writer.WriteAsync(
            $"  <url>\n    <loc>{webBaseUrl}/posts/{postSlug}</loc>\n    <lastmod>{updatedAt.UtcDateTime:yyyy-MM-dd}</lastmod>\n    <changefreq>weekly</changefreq>\n    <priority>0.8</priority>\n  </url>\n"
        );
    }

    await writer.WriteLineAsync("</urlset>");
});

app.MapGet("/posts/{id:guid}", async (
    Guid id,
    HttpRequest request,
    Db db,
    RedisService redis,
    IConfiguration configuration
) =>
{
    var webBaseUrl = GetWebBaseUrl(configuration);

    if (!IsCrawler(request.Headers.UserAgent.ToString()))
    {
        return Results.Redirect($"{webBaseUrl}/posts/{id}");
    }

    var cacheKey = $"crawler:post:{id}";
    var cached = await redis.GetAsync<string>(cacheKey);
    if (cached is not null)
        return Results.Content(cached, "text/html; charset=utf-8");

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT title, content, image_url, vote_count_hakli, vote_count_haksiz,
               COALESCE(slug, id::text), created_at
        FROM posts
        WHERE id = @id AND status = 'active'
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Redirect(webBaseUrl);

    var title = reader.GetString(0);
    var content = reader.GetString(1);
    var customImageUrl = reader.IsDBNull(2) ? null : reader.GetString(2);
    var hakli = reader.GetInt32(3);
    var haksiz = reader.GetInt32(4);
    var slug = reader.GetString(5);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(6);

    var html = BuildOgHtml(id, slug, title, content, hakli, haksiz, customImageUrl, webBaseUrl, createdAt);
    await redis.SetAsync(cacheKey, html, TimeSpan.FromMinutes(5));
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/posts/{slug}", async (
    string slug,
    HttpRequest request,
    Db db,
    RedisService redis,
    IConfiguration configuration
) =>
{
    var webBaseUrl = GetWebBaseUrl(configuration);

    if (!IsCrawler(request.Headers.UserAgent.ToString()))
    {
        return Results.Redirect($"{webBaseUrl}/posts/{slug}");
    }

    var cacheKey = $"crawler:slug:{slug}";
    var cached = await redis.GetAsync<string>(cacheKey);
    if (cached is not null)
        return Results.Content(cached, "text/html; charset=utf-8");

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT id, title, content, image_url, vote_count_hakli, vote_count_haksiz, created_at
        FROM posts
        WHERE slug = @slug AND status = 'active'
        """,
        connection
    );
    command.Parameters.AddWithValue("slug", slug);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Redirect(webBaseUrl);

    var id = reader.GetGuid(0);
    var title = reader.GetString(1);
    var content = reader.GetString(2);
    var customImageUrl = reader.IsDBNull(3) ? null : reader.GetString(3);
    var hakli = reader.GetInt32(4);
    var haksiz = reader.GetInt32(5);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(6);

    var html = BuildOgHtml(id, slug, title, content, hakli, haksiz, customImageUrl, webBaseUrl, createdAt);
    await redis.SetAsync(cacheKey, html, TimeSpan.FromMinutes(5));
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/api/v1/posts/{id:guid}/share-image.png", async (
    Guid id,
    Db db,
    RedisService redis,
    ShareImageService shareImageService,
    HttpContext httpContext
) =>
{
    var cacheKey = $"post:share-image:{id}";
    var cached = await redis.GetAsync<byte[]>(cacheKey);
    if (cached is not null)
    {
        httpContext.Response.Headers.CacheControl = "public, max-age=600, stale-while-revalidate=60";
        return Results.Bytes(cached, "image/png");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT p.title, c.name, p.vote_count_hakli, p.vote_count_haksiz
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        WHERE p.id = @id AND p.status = 'active'
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();

    var title = reader.GetString(0);
    var category = reader.GetString(1);
    var hakli = reader.GetInt32(2);
    var haksiz = reader.GetInt32(3);

    var bytes = await shareImageService.GeneratePostCardAsync(title, category, hakli, haksiz);
    if (bytes is null) return Results.StatusCode(500);

    await redis.SetAsync(cacheKey, bytes, TimeSpan.FromMinutes(10));
    httpContext.Response.Headers.CacheControl = "public, max-age=600, stale-while-revalidate=60";
    return Results.Bytes(bytes, "image/png");
});

app.MapGet("/api/v1/posts/{id:guid}/story-image.png", async (
    Guid id,
    Db db,
    RedisService redis,
    ShareImageService shareImageService,
    HttpContext httpContext
) =>
{
    var cacheKey = $"post:story-image:{id}";
    var cached = await redis.GetAsync<byte[]>(cacheKey);
    if (cached is not null)
    {
        httpContext.Response.Headers.CacheControl = "public, max-age=600, stale-while-revalidate=60";
        return Results.Bytes(cached, "image/png");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT p.title, c.name, p.vote_count_hakli, p.vote_count_haksiz
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        WHERE p.id = @id AND p.status = 'active'
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();

    var title = reader.GetString(0);
    var category = reader.GetString(1);
    var hakli = reader.GetInt32(2);
    var haksiz = reader.GetInt32(3);

    var bytes = await shareImageService.GenerateStoryCardAsync(title, category, hakli, haksiz);
    if (bytes is null) return Results.StatusCode(500);

    await redis.SetAsync(cacheKey, bytes, TimeSpan.FromMinutes(10));
    httpContext.Response.Headers.CacheControl = "public, max-age=600, stale-while-revalidate=60";
    return Results.Bytes(bytes, "image/png");
});

// Returns a time-limited nonce for Play Integrity / App Attest verification.
app.MapGet("/api/v1/devices/nonce", async (PlayIntegrityService integrity) =>
{
    var nonce = await integrity.GenerateNonceAsync();
    return Results.Ok(new { nonce });
});

app.MapPost("/api/v1/devices/register", async (
    RegisterDeviceRequest request,
    HttpRequest httpRequest,
    Db db,
    DeviceTokenService tokenService,
    RedisService redis,
    AppAttestationService appAttestation,
    ILogger<Program> log
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (!IsSupportedPlatform(request.Platform))
    {
        return BadRequest("INVALID_PLATFORM", "Platform android, ios veya web olmalı.");
    }

    // /24 subnet rate limit: max 5 registrations per hour per subnet
    var subnetBlock = GetClientIpBlock(httpRequest);
    if (subnetBlock is not null)
    {
        var rateLimitKey = $"device_reg:subnet:{subnetBlock}";
        var db2 = redis.GetDb();
        var count = await db2.StringIncrementAsync(rateLimitKey);
        if (count == 1)
        {
            await db2.KeyExpireAsync(rateLimitKey, TimeSpan.FromHours(1));
        }
        if (count > 5)
        {
            log.LogWarning("DeviceRegister: /24 subnet rate limit aşıldı. Subnet={S}", subnetBlock);
            return TooManyRequests("SUBNET_RATE_LIMIT", "Bu ağdan çok fazla kayıt yapıldı. Lütfen bir saat sonra tekrar deneyin.");
        }
    }

    var token = tokenService.Generate();
    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO devices (fingerprint, platform, app_version, device_token)
        VALUES (@fingerprint, @platform, @appVersion, @token)
        RETURNING id
        """,
        connection
    );
    command.Parameters.AddWithValue("fingerprint", request.Fingerprint);
    command.Parameters.AddWithValue("platform", request.Platform);
    command.Parameters.AddWithValue("appVersion", request.AppVersion);
    command.Parameters.AddWithValue("token", token);

    var deviceId = (Guid)(await command.ExecuteScalarAsync()
        ?? throw new InvalidOperationException("Device insert failed."));

    var attestationDecision = await appAttestation.VerifyDeviceRegistrationAsync(
        httpRequest,
        connection,
        deviceId,
        request.Platform,
        request.IntegrityToken,
        request.Nonce);
    if (attestationDecision.ShouldBlock)
    {
        log.LogWarning("DeviceRegister: App attestation hard-enforce blocked. Fingerprint={F}", request.Fingerprint);
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("APP_ATTESTATION_FAILED", "Uygulama doğrulaması başarısız.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    return Results.Created(
        "/api/v1/devices/register",
        new DeviceSession(deviceId, token, DateTimeOffset.UtcNow.AddYears(1))
    );
});

app.MapPut("/api/v1/devices/fcm-token", async (
    FcmTokenRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (!IsSupportedPlatform(request.Platform))
    {
        return BadRequest("INVALID_PLATFORM", "Platform android, ios veya web olmalÄ±.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO fcm_tokens (device_id, token, platform)
        VALUES (@deviceId, @token, @platform)
        ON CONFLICT (token)
        DO UPDATE SET device_id = @deviceId, platform = @platform, updated_at = NOW()
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    command.Parameters.AddWithValue("token", request.Token);
    command.Parameters.AddWithValue("platform", request.Platform);
    await command.ExecuteNonQueryAsync();

    return Results.NoContent();
});

app.MapDelete("/api/v1/devices/fcm-token", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "DELETE FROM fcm_tokens WHERE device_id = @deviceId",
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();

    return Results.NoContent();
});

// Public moderation transparency report — cached 6h, no auth required.
app.MapGet("/api/v1/moderation/transparency", async (Db db, RedisService redis) =>
{
    const string cacheKey = "moderation:transparency";
    var cached = await redis.GetAsync<ModerationTransparencyResponse>(cacheKey);
    if (cached is not null) return Results.Ok(cached);

    await using var connection = await db.OpenConnectionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        SELECT
            (SELECT COUNT(*) FROM posts  WHERE created_at >= NOW() - INTERVAL '30 days')                    AS posts_created,
            (SELECT COUNT(*) FROM posts  WHERE status = 'removed' AND updated_at >= NOW() - INTERVAL '30 days') AS posts_removed,
            (SELECT COUNT(*) FROM comments WHERE created_at >= NOW() - INTERVAL '30 days')                  AS comments_created,
            (SELECT COUNT(*) FROM comments WHERE is_deleted = true AND updated_at >= NOW() - INTERVAL '30 days') AS comments_removed,
            (SELECT COUNT(*) FROM reports WHERE created_at >= NOW() - INTERVAL '30 days')                   AS reports_received,
            (SELECT COUNT(*) FROM reports WHERE status IN ('resolved','actioned') AND updated_at >= NOW() - INTERVAL '30 days') AS reports_actioned,
            (SELECT COUNT(*) FROM reports r WHERE r.created_at >= NOW() - INTERVAL '30 days' AND r.reason = 'harassment')    AS reason_harassment,
            (SELECT COUNT(*) FROM reports r WHERE r.created_at >= NOW() - INTERVAL '30 days' AND r.reason = 'hate_speech')   AS reason_hate_speech,
            (SELECT COUNT(*) FROM reports r WHERE r.created_at >= NOW() - INTERVAL '30 days' AND r.reason = 'spam')          AS reason_spam,
            (SELECT COUNT(*) FROM reports r WHERE r.created_at >= NOW() - INTERVAL '30 days' AND r.reason = 'misinformation') AS reason_misinformation,
            (SELECT COUNT(*) FROM reports r WHERE r.created_at >= NOW() - INTERVAL '30 days' AND r.reason NOT IN ('harassment','hate_speech','spam','misinformation')) AS reason_other
        """,
        connection);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Ok(new ModerationTransparencyResponse());

    var postsCreated  = reader.GetInt64(0);
    var postsRemoved  = reader.GetInt64(1);
    var commentsCreated  = reader.GetInt64(2);
    var commentsRemoved  = reader.GetInt64(3);
    var reportsReceived  = reader.GetInt64(4);
    var reportsActioned  = reader.GetInt64(5);

    var response = new ModerationTransparencyResponse(
        PeriodDays: 30,
        PostsCreated: postsCreated,
        PostsRemoved: postsRemoved,
        PostRemovalRatePercent: postsCreated > 0
            ? Math.Round(postsRemoved * 100.0 / postsCreated, 1) : 0,
        CommentsCreated: commentsCreated,
        CommentsRemoved: commentsRemoved,
        CommentRemovalRatePercent: commentsCreated > 0
            ? Math.Round(commentsRemoved * 100.0 / commentsCreated, 1) : 0,
        ReportsReceived: reportsReceived,
        ReportsActioned: reportsActioned,
        ReportActionRatePercent: reportsReceived > 0
            ? Math.Round(reportsActioned * 100.0 / reportsReceived, 1) : 0,
        RemovalReasons: new ModerationReasons(
            Harassment: reader.GetInt64(6),
            HateSpeech: reader.GetInt64(7),
            Spam: reader.GetInt64(8),
            Misinformation: reader.GetInt64(9),
            Other: reader.GetInt64(10)
        ),
        LastUpdated: DateTimeOffset.UtcNow
    );

    await redis.SetAsync(cacheKey, response, TimeSpan.FromHours(6));
    return Results.Ok(response);
});

app.MapGet("/api/v1/categories", async (Db db, RedisService redis) =>
{
    var cached = await redis.GetAsync<CategoriesResponse>(CacheKeys.Categories);
    if (cached is not null) return Results.Ok(cached);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT id,
               name,
               emoji,
               CASE id
                   WHEN 1 THEN 'is-hayati'
                   WHEN 2 THEN 'iliskiler'
                   WHEN 3 THEN 'aile'
                   WHEN 4 THEN 'arkadaslik'
                   ELSE 'diger'
               END AS slug
        FROM categories
        ORDER BY sort_order
        """,
        connection
    );

    var categories = new List<CategoryDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        categories.Add(new CategoryDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3)
        ));
    }

    var response = new CategoriesResponse(categories);
    await redis.SetAsync(CacheKeys.Categories, response, TimeSpan.FromHours(1));
    return Results.Ok(response);
});

app.MapPost("/api/v1/categories/{id:int}/follow", async (
    int id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO followed_categories (user_id, category_id)
        SELECT @userId, @categoryId
        WHERE EXISTS (SELECT 1 FROM categories WHERE id = @categoryId)
        ON CONFLICT DO NOTHING
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("categoryId", id);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/categories/{id:int}/mute", async (
    int id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO muted_categories (user_id, category_id)
        SELECT @userId, @categoryId
        WHERE EXISTS (SELECT 1 FROM categories WHERE id = @categoryId)
        ON CONFLICT DO NOTHING
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("categoryId", id);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapDelete("/api/v1/categories/{id:int}/mute", async (
    int id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "DELETE FROM muted_categories WHERE user_id = @userId AND category_id = @categoryId",
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("categoryId", id);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapDelete("/api/v1/categories/{id:int}/follow", async (
    int id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "DELETE FROM followed_categories WHERE user_id = @userId AND category_id = @categoryId",
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("categoryId", id);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/posts", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis,
    int page = 1,
    int limit = 20,
    int? categoryId = null,
    string sort = "trending",
    Guid? afterId = null,
    string? cursor = null
) =>
{
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
    var decoded = sort == "trending" ? DecodeCursor(cursor) : null;
    var hasCursor = decoded is not null;
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    // Anonim istekler iÃ§in feed cache (60s trending, 30s new).
    // GiriÅŸ yapmÄ±ÅŸ / cihaz tanÄ±mlÄ± kullanÄ±cÄ±lar her zaman DB'ye gider (kiÅŸisel oy durumu).
    var isAnonymous = deviceId is null && userId is null;
    FeedResponse? feedCached = null;
    string? feedCacheKey = null;

    if (isAnonymous && limit == 20 && afterId is null)
    {
        feedCacheKey = categoryId is null
            ? (sort == "new" ? CacheKeys.FeedNew(page) : CacheKeys.FeedTrending(page))
            : CacheKeys.FeedCategory(categoryId.Value, sort, page);
        feedCached = await redis.GetAsync<FeedResponse>(feedCacheKey);
        if (feedCached is not null)
            return Results.Ok(feedCached);
    }

    var orderBy = sort == "new" ? "p.created_at DESC" : "p.trend_score DESC, p.created_at DESC";
    var categoryWhere = categoryId is null ? "" : "AND p.category_id = @categoryId";
    var afterWhere = hasCursor
        ? "AND (p.trend_score < @cursorScore OR (p.trend_score = @cursorScore AND p.id < @cursorId))"
        : afterId is null
            ? ""
            : "AND p.created_at > COALESCE((SELECT anchor.created_at FROM posts anchor WHERE anchor.id = @afterId), 'infinity'::timestamptz)";

    // Not-interested filter: load device's dismissed post IDs from Redis
    var notInterestedIds = deviceId is not null
        ? await redis.GetNotInterestedPostsAsync(deviceId.Value)
        : null;
    var hasNotInterested = notInterestedIds is { Count: > 0 };
    var notInterestedWhere = hasNotInterested ? "AND p.id != ALL(@notInterestedIds)" : "";

    // Re-impression limit: exclude posts already shown ≥2 times in last 24h
    var overImposedIds = deviceId is not null
        ? await redis.GetOverImposedPostsAsync(deviceId.Value)
        : null;
    var hasOverImposed = overImposedIds is { Count: > 0 };
    var overImposedWhere = hasOverImposed ? "AND p.id != ALL(@overImposedIds)" : "";

    // Distribution stage filter:
    // - Stage 1: shown through UCB-ranked exploration slots instead of random hash sampling.
    // - Stage 2: shown in category-specific feeds
    // - Stage 3: shown in main trending feed
    var stage1UcbSlots = Math.Max(1, (int)Math.Ceiling(limit * 0.10));
    var stage1UcbWhere = """
        p.distribution_stage = 1
        AND p.id IN (
            SELECT ranked_stage1.id
            FROM (
                SELECT sp.id,
                       (
                           (sp.vote_count_hakli + sp.vote_count_haksiz)::double precision
                           / GREATEST(COALESCE(pv.exposures, 0), 1)
                       )
                       + SQRT(
                           2.0
                           * LN(totals.total_exposures + 1.0)
                           / GREATEST(COALESCE(pv.exposures, 0), 1)
                       ) AS ucb_score
                FROM posts sp
                LEFT JOIN (
                    SELECT post_id, SUM(view_count)::double precision AS exposures
                    FROM post_views
                    GROUP BY post_id
                ) pv ON pv.post_id = sp.id
                CROSS JOIN (
                    SELECT GREATEST(COALESCE(SUM(pv_total.view_count), 0), 1)::double precision AS total_exposures
                    FROM posts total_posts
                    LEFT JOIN post_views pv_total ON pv_total.post_id = total_posts.id
                    WHERE total_posts.distribution_stage = 1
                      AND total_posts.status = 'active'
                      AND total_posts.is_unlisted = FALSE
                ) totals
                WHERE sp.distribution_stage = 1
                  AND sp.status = 'active'
                  AND sp.is_unlisted = FALSE
                ORDER BY ucb_score DESC, sp.created_at DESC
                LIMIT @stage1UcbSlots
            ) ranked_stage1
        )
        """;
    var stageWhere = categoryId is not null
        ? $"AND (p.distribution_stage >= 2 OR ({stage1UcbWhere}))"
        : $"AND (p.distribution_stage >= 3 OR ({stage1UcbWhere}))";

    // Diversity pass applies only to page 1, trending sort, no category filter, no cursor of any kind.
    // Uses window functions to cap: ≤5 posts per category, ≤3 posts per author/device.
    var applyDiversityPass = page == 1 && sort == "trending" && categoryId is null && afterId is null && !hasCursor;

    await using var connection = await db.OpenConnectionAsync();

    string countSql;
    if (applyDiversityPass)
    {
        countSql = $"""
            WITH base AS (
                SELECT p.id,
                       ROW_NUMBER() OVER (PARTITION BY p.category_id ORDER BY p.trend_score DESC, p.created_at DESC) AS cat_rank,
                       ROW_NUMBER() OVER (PARTITION BY COALESCE(p.user_id::text, p.device_id::text) ORDER BY p.trend_score DESC, p.created_at DESC) AS author_rank
                FROM posts p
                WHERE p.status = 'active' AND p.is_unlisted = FALSE {stageWhere} {notInterestedWhere} {overImposedWhere}
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            )
            SELECT COUNT(*) FROM base WHERE cat_rank <= 5 AND author_rank <= 3
            """;
    }
    else
    {
        countSql = $"""
            SELECT COUNT(*)
            FROM posts p
            WHERE p.status = 'active' AND p.is_unlisted = FALSE {categoryWhere} {afterWhere} {stageWhere} {notInterestedWhere} {overImposedWhere}
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
              AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            """;
    }

    // Cursor-based pagination skips the total-count query (keyset queries don't have a cheap total).
    int total = 0;
    if (!hasCursor)
    {
        await using var countCommand = new NpgsqlCommand(countSql, connection);
        countCommand.Parameters.AddWithValue("userId", userParam);
        countCommand.Parameters.AddWithValue("stage1UcbSlots", stage1UcbSlots);
        if (deviceId is not null) countCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
        if (!applyDiversityPass && categoryId is not null) countCommand.Parameters.AddWithValue("categoryId", categoryId.Value);
        if (!applyDiversityPass && afterId is not null) countCommand.Parameters.AddWithValue("afterId", afterId.Value);
        if (hasNotInterested) countCommand.Parameters.AddWithValue("notInterestedIds", notInterestedIds!.ToArray());
        if (hasOverImposed) countCommand.Parameters.AddWithValue("overImposedIds", overImposedIds!.ToArray());
        total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
    }

    string feedSql;
    if (applyDiversityPass)
    {
        feedSql = $"""
            WITH base AS (
                SELECT p.id, p.title, p.content, p.image_url, c.id AS cat_id, c.name AS cat_name, c.emoji,
                       p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                       v.vote_type, p.trend_score, p.created_at,
                       (p.device_id = @deviceId OR p.user_id = @userId),
                       p.is_anonymous,
                       ROW_NUMBER() OVER (PARTITION BY p.category_id ORDER BY p.trend_score DESC, p.created_at DESC) AS cat_rank,
                       ROW_NUMBER() OVER (PARTITION BY COALESCE(p.user_id::text, p.device_id::text) ORDER BY p.trend_score DESC, p.created_at DESC) AS author_rank
                FROM posts p
                JOIN categories c ON c.id = p.category_id
                LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
                WHERE p.status = 'active' AND p.is_unlisted = FALSE {stageWhere} {notInterestedWhere} {overImposedWhere}
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            )
            SELECT id, title, content, image_url, cat_id, cat_name, emoji,
                   vote_count_hakli, vote_count_haksiz, comment_count,
                   vote_type, trend_score, created_at, is_owner, is_anonymous
            FROM base
            WHERE cat_rank <= 5 AND author_rank <= 3
            ORDER BY trend_score DESC, created_at DESC
            LIMIT @limit
            """;
    }
    else
    {
        // For trending sort apply affinity multiplier: user_category_affinity for authenticated
        // users, device_category_affinity for anonymous devices.
        var affinityJoin = sort == "trending" && userId is not null
            ? "LEFT JOIN user_category_affinity uca ON uca.user_id = @userId AND uca.category_id = p.category_id"
            : sort == "trending" && deviceId is not null
                ? "LEFT JOIN device_category_affinity uca ON uca.device_id = @deviceId AND uca.category_id = p.category_id"
                : "";
        var affinityScore = sort == "trending" && (userId is not null || deviceId is not null)
            ? "p.trend_score * (1.0 + 0.5 * COALESCE(uca.score / 10.0, 0))"
            : "p.trend_score";
        var affinityOrderBy = sort == "trending"
            ? $"{affinityScore} DESC, p.created_at DESC"
            : orderBy;

        feedSql = $"""
            SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
                   p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                   v.vote_type, p.trend_score, p.created_at,
                   (p.device_id = @deviceId OR p.user_id = @userId),
                   p.is_unlisted, p.is_anonymous, p.tags, p.content_source
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            {affinityJoin}
            WHERE p.status = 'active' AND p.is_unlisted = FALSE {categoryWhere} {afterWhere} {stageWhere} {notInterestedWhere} {overImposedWhere}
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
              AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            ORDER BY {affinityOrderBy}
            LIMIT @limit{(hasCursor ? "" : " OFFSET @offset")}
            """;
    }

    var freshSlotTarget = applyDiversityPass ? Math.Max(1, (int)Math.Ceiling(limit * 0.20)) : 0;

    await using var command = new NpgsqlCommand(feedSql, connection);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("stage1UcbSlots", stage1UcbSlots);
    if (!applyDiversityPass && categoryId is not null) command.Parameters.AddWithValue("categoryId", categoryId.Value);
    if (!applyDiversityPass && afterId is not null) command.Parameters.AddWithValue("afterId", afterId.Value);
    if (hasCursor && decoded is not null)
    {
        command.Parameters.AddWithValue("cursorScore", decoded.Value.TrendScore);
        command.Parameters.AddWithValue("cursorId", decoded.Value.Id);
    }
    if (hasNotInterested) command.Parameters.AddWithValue("notInterestedIds", notInterestedIds!.ToArray());
    if (hasOverImposed) command.Parameters.AddWithValue("overImposedIds", overImposedIds!.ToArray());
    command.Parameters.AddWithValue("limit", applyDiversityPass ? Math.Max(1, limit - freshSlotTarget) : limit);
    if (!applyDiversityPass && !hasCursor) command.Parameters.AddWithValue("offset", offset);

    var posts = await ReadPostsAsync(command);
    posts = LabelPosts(
        posts,
        sort == "new" ? "fresh" : sort == "trending" && userId is not null && !applyDiversityPass ? "category_affinity" : "trending",
        sort == "new" ? "Yeni tartışma" : sort == "trending" && userId is not null && !applyDiversityPass ? "İlgilendiğin kategoriden" : "Toplulukta öne çıkıyor"
    );

    // Epsilon-greedy: guarantee ~20% fresh posts (<2h, not already shown) at evenly-spaced positions.
    if (applyDiversityPass && freshSlotTarget > 0)
    {
        var existingIds = posts.Select(p => p.Id).ToHashSet();

        await using var freshCmd = new NpgsqlCommand(
            $"""
            SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
                   p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                   v.vote_type, p.trend_score, p.created_at,
                   (p.device_id = @deviceId OR p.user_id = @userId),
                   p.is_unlisted, p.is_anonymous, p.tags, p.content_source
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            WHERE p.status = 'active' AND p.is_unlisted = FALSE
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
              AND p.created_at >= NOW() - INTERVAL '2 hours'
              AND p.id != ALL(@existingIds)
              AND p.distribution_stage >= 2
              {notInterestedWhere} {overImposedWhere}
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
              AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            ORDER BY p.created_at DESC
            LIMIT @freshSlots
            """,
            connection
        );
        freshCmd.Parameters.AddWithValue("deviceId", deviceParam);
        freshCmd.Parameters.AddWithValue("userId", userParam);
        freshCmd.Parameters.AddWithValue("existingIds", existingIds.ToArray());
        freshCmd.Parameters.AddWithValue("freshSlots", freshSlotTarget);
        if (hasNotInterested) freshCmd.Parameters.AddWithValue("notInterestedIds", notInterestedIds!.ToArray());
        if (hasOverImposed) freshCmd.Parameters.AddWithValue("overImposedIds", overImposedIds!.ToArray());

        var freshPosts = LabelPosts(await ReadPostsAsync(freshCmd), "fresh", "Yeni tartışma");

        // Interleave fresh posts at evenly-spaced positions
        var mutable = posts.ToList();
        var step = freshPosts.Count > 0 ? Math.Max(1, mutable.Count / freshPosts.Count) : 0;
        for (int i = 0; i < freshPosts.Count; i++)
        {
            var insertAt = Math.Min(step * (i + 1) + i, mutable.Count);
            mutable.Insert(insertAt, freshPosts[i]);
        }
        posts = mutable;

        // Serendipity: every 10 posts, inject 1-2 posts from categories with no user interaction in last 7 days
        if (userId is not null && posts.Count >= 10)
        {
            var seenCategories = posts.Select(p => p.Category.Id).ToHashSet();
            var serendipitySlots = Math.Max(1, posts.Count / 10);
            var serendipityIds = posts.Select(p => p.Id).ToHashSet();

            await using var serendipityCmd = new NpgsqlCommand(
                """
                SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
                       p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                       v.vote_type, p.trend_score, p.created_at,
                       (p.device_id = @deviceId OR p.user_id = @userId),
                       p.is_anonymous
                FROM posts p
                JOIN categories c ON c.id = p.category_id
                LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
                WHERE p.status = 'active' AND p.is_unlisted = FALSE
                  AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
                  AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
                  AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
                  AND p.distribution_stage >= 2
                  AND p.id != ALL(@existingIds)
                  AND p.category_id NOT IN (
                      SELECT DISTINCT category_id
                      FROM user_category_affinity
                      WHERE user_id = @userId
                        AND updated_at >= NOW() - INTERVAL '7 days'
                  )
                  AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
                ORDER BY RANDOM()
                LIMIT @slots
                """,
                connection
            );
            serendipityCmd.Parameters.AddWithValue("deviceId", deviceParam);
            serendipityCmd.Parameters.AddWithValue("userId", userParam);
            serendipityCmd.Parameters.AddWithValue("existingIds", serendipityIds.ToArray());
            serendipityCmd.Parameters.AddWithValue("slots", serendipitySlots);

            var serendipityPosts = LabelPosts(await ReadPostsAsync(serendipityCmd), "serendipity", "Farklı bir kategoriden");
            if (serendipityPosts.Count > 0)
            {
                var withSerendipity = posts.ToList();
                for (int i = 0; i < serendipityPosts.Count; i++)
                {
                    var insertAt = Math.Min(10 * (i + 1) - 1, withSerendipity.Count);
                    withSerendipity.Insert(insertAt, serendipityPosts[i]);
                }
                posts = withSerendipity;
            }
        }
    }

    var rankingLabel = (sort, categoryId) switch
    {
        ("new", null) => "new",
        ("new", not null) => "category_new",
        (_, not null) => "category_trending",
        _ => "trending"
    };

    var hasNextPage = hasCursor ? posts.Count >= limit : offset + posts.Count < total;
    var nextCursor = sort == "trending" && posts.Count > 0 && hasNextPage
        ? EncodeCursor(posts[^1].TrendScore, posts[^1].Id)
        : null;
    var response = new FeedResponse(
        posts,
        new Pagination(page, limit, hasCursor ? 0 : total, hasNextPage),
        rankingLabel,
        nextCursor);

    if (isAnonymous && limit == 20 && feedCacheKey is not null)
    {
        var ttl = sort == "new" ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(60);
        await redis.SetAsync(feedCacheKey, response, ttl);
    }

    // Record impressions for re-impression limiting (fire-and-forget, skip for anonymous)
    if (deviceId is not null && posts.Count > 0)
        _ = redis.RecordImpressionsAsync(deviceId.Value, posts.Select(p => p.Id));

    return Results.Ok(response);
});

app.MapGet("/api/v1/posts/discover", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis,
    GeoService geo
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();

    // Rising: stage >= 2, < 6h old, >= 15 votes, ordered by votes-per-minute velocity
    await using var risingCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
          AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
          AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
              )
          )
          AND p.distribution_stage >= 2
          AND p.created_at >= NOW() - INTERVAL '6 hours'
          AND (p.vote_count_hakli + p.vote_count_haksiz) >= 15
        ORDER BY (p.vote_count_hakli + p.vote_count_haksiz)::float
                 / GREATEST(EXTRACT(EPOCH FROM (NOW() - p.created_at)) / 60.0, 1) DESC
        LIMIT 10
        """,
        connection
    );
    risingCmd.Parameters.AddWithValue("deviceId", deviceParam);
    risingCmd.Parameters.AddWithValue("userId", userParam);
    var rising = LabelPosts(await ReadPostsAsync(risingCmd), "rising", "Hızla oy alıyor");

    // Controversial: stage >= 2, total > 40, |hakli-haksiz|/total < 0.2
    // Ordered by controversy_score = SQRT(total) * min(hakli,haksiz) / max(hakli,haksiz)
    await using var controversialCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
          AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
          AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
              )
          )
          AND p.distribution_stage >= 2
          AND (p.vote_count_hakli + p.vote_count_haksiz) > 40
          AND ABS(p.vote_count_hakli - p.vote_count_haksiz)::float
              / NULLIF(p.vote_count_hakli + p.vote_count_haksiz, 0) < 0.2
        ORDER BY POWER((p.vote_count_hakli + p.vote_count_haksiz)::float, 0.5)
                 * LEAST(p.vote_count_hakli, p.vote_count_haksiz)::float
                 / GREATEST(p.vote_count_hakli, p.vote_count_haksiz, 1) DESC
        LIMIT 10
        """,
        connection
    );
    controversialCmd.Parameters.AddWithValue("deviceId", deviceParam);
    controversialCmd.Parameters.AddWithValue("userId", userParam);
    var controversial = LabelPosts(await ReadPostsAsync(controversialCmd), "controversial", "Topluluk ikiye bölündü");

    // Fresh: stage >= 2, < 2h old, 0-10 votes — new posts awaiting judgment
    await using var freshCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
          AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
          AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
              )
          )
          AND p.distribution_stage >= 2
          AND p.created_at >= NOW() - INTERVAL '2 hours'
          AND (p.vote_count_hakli + p.vote_count_haksiz) BETWEEN 0 AND 10
        ORDER BY p.created_at DESC
        LIMIT 10
        """,
        connection
    );
    freshCmd.Parameters.AddWithValue("deviceId", deviceParam);
    freshCmd.Parameters.AddWithValue("userId", userParam);
    var fresh = LabelPosts(await ReadPostsAsync(freshCmd), "fresh", "Yeni tartışma");

    // City trending (optional — requires GeoLite2 database)
    IReadOnlyList<PostDto>? cityTrending = null;
    string? city = null;
    var voterIp = httpRequest.HttpContext.Connection.RemoteIpAddress;
    city = geo.GetCity(voterIp);
    if (city is not null)
    {
        var cityPostIds = await redis.GetCityTrendingPostIdsAsync(city, 10);
        if (cityPostIds.Count > 0)
        {
            await using var cityCmd = new NpgsqlCommand(
                """
                SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
                       p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                       v.vote_type, p.trend_score, p.created_at,
                       (p.device_id = @deviceId OR p.user_id = @userId),
                       p.is_anonymous
                FROM posts p
                JOIN categories c ON c.id = p.category_id
                LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
                WHERE p.id = ANY(@postIds) AND p.status = 'active' AND p.is_unlisted = FALSE
                  AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
                  AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
                  AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
                  AND (
                      @userId = '00000000-0000-0000-0000-000000000000'::uuid
                      OR (
                          NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                          AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
                      )
                  )
                ORDER BY p.trend_score DESC
                """,
                connection
            );
            cityCmd.Parameters.AddWithValue("deviceId", deviceParam);
            cityCmd.Parameters.AddWithValue("userId", userParam);
            cityCmd.Parameters.AddWithValue("postIds", cityPostIds.ToArray());
            cityTrending = LabelPosts(await ReadPostsAsync(cityCmd), "trending", "Toplulukta öne çıkıyor");
        }
    }

    // Serendipity: posts from categories the user/device hasn't interacted with in the past 7 days.
    // Exposes users to new categories — "Farklı Bir Şey" section in the Keşfet view.
    await using var serendipityCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
          AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
          AND (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
              )
          )
          AND p.distribution_stage >= 2
          AND p.category_id NOT IN (
              SELECT uca.category_id
              FROM user_category_affinity uca
              WHERE uca.user_id = @userId
                AND uca.updated_at >= NOW() - INTERVAL '7 days'
              UNION
              SELECT dca.category_id
              FROM device_category_affinity dca
              WHERE dca.device_id = @deviceId
                AND dca.updated_at >= NOW() - INTERVAL '7 days'
          )
        ORDER BY p.trend_score DESC
        LIMIT 10
        """,
        connection
    );
    serendipityCmd.Parameters.AddWithValue("deviceId", deviceParam);
    serendipityCmd.Parameters.AddWithValue("userId", userParam);
    var serendipity = LabelPosts(await ReadPostsAsync(serendipityCmd), "serendipity", "Farklı bir şey");

    return Results.Ok(new DiscoverResponse(rising, controversial, fresh, cityTrending, city, serendipity));
});

// Cursor-based immersive discover feed (Reels-style vertical scroll).
// UCB Stage 1 slots: 10% of feed reserved for new/unproven posts (exploration).
// Diversity pass: max 3 per category, max 2 consecutive same category, no consecutive same author.
// Fresh slot: target max(1, 20% of mainSlots) posts <2h old, stage>=2. Safety guardrail: ≥1 controversial-but-safe per 10 items.
// not_interested categories suppressed for 7 days (Redis).
app.MapGet("/api/v1/posts/discover/feed", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis,
    string? cursor = null,
    int limit = 10,
    string? seenPostIds = null
) =>
{
    limit = Math.Clamp(limit, 1, 20);
    var ucbSlots = Math.Max(1, limit / 10);
    var mainSlots = limit - ucbSlots;

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    var hasCursor = cursor is not null;
    var cursorDecoded = DecodeCursor(cursor);
    var cursorScore = cursorDecoded?.TrendScore ?? 0.0;
    var cursorId = cursorDecoded?.Id ?? Guid.Empty;

    // Suppressed post IDs (not_interested 30d + skip 24h + client-sent seenPostIds) ve category IDs (7d)
    HashSet<Guid> suppressedPostIds = [];
    HashSet<int> suppressedCategoryIds = [];
    if (deviceId is not null)
    {
        var notInterested = await redis.GetNotInterestedPostsAsync(deviceId.Value);
        var skipSuppressed = await redis.GetSkipSuppressedPostsAsync(deviceId.Value);
        var seenInSession = await redis.GetSeenDiscoverPostIdsAsync(deviceId.Value);
        suppressedPostIds = notInterested;
        suppressedPostIds.UnionWith(skipSuppressed);
        suppressedPostIds.UnionWith(seenInSession);
        suppressedCategoryIds = await redis.GetNotInterestedCategoriesAsync(deviceId.Value);
    }
    // Client tarafından gönderilen seen_post_ids (aynı oturumda görülenler, max 100)
    if (!string.IsNullOrEmpty(seenPostIds))
    {
        foreach (var idStr in seenPostIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(100))
        {
            if (Guid.TryParse(idStr, out var seenId))
                suppressedPostIds.Add(seenId);
        }
    }
    var hasSuppressedPosts = suppressedPostIds.Count > 0;
    var hasSuppressedCategories = suppressedCategoryIds.Count > 0;
    var suppressedPostsWhere = hasSuppressedPosts ? "AND p.id != ALL(@suppressedIds)" : "";
    var suppressedCategoriesWhere = hasSuppressedCategories ? "AND p.category_id != ALL(@suppressedCategoryIds)" : "";

    await using var connection = await db.OpenConnectionAsync();

    // Main candidates (distribution_stage >= 2), 3× limit for diversity pass
    await using var cmd = new NpgsqlCommand(
        $"""
        WITH candidate AS (
            SELECT p.id, p.title, p.content, p.image_url,
                   c.id AS c_id, c.name AS c_name, c.emoji,
                   p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                   v.vote_type, p.trend_score, p.created_at,
                   (p.device_id = @deviceId OR p.user_id = @userId) AS is_owner,
                   p.is_unlisted, p.is_anonymous, p.tags, p.content_source,
                   COALESCE(p.user_id, p.device_id) AS ranking_author_key,
                   ROW_NUMBER() OVER (
                       PARTITION BY COALESCE(p.user_id::text, p.device_id::text)
                       ORDER BY p.trend_score DESC, p.id DESC
                   ) AS author_rank
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            WHERE p.status = 'active'
              AND p.is_unlisted = FALSE
              AND p.distribution_stage >= 2
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (
                  SELECT COUNT(*) FROM reports r
                  WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending'
              ) < 3
              AND (
                  @deviceId = '00000000-0000-0000-0000-000000000000'::uuid
                  OR NOT EXISTS (
                      SELECT 1 FROM post_views pv
                      WHERE pv.post_id = p.id AND pv.device_id = @deviceId
                  )
              )
              AND (
                  @userId = '00000000-0000-0000-0000-000000000000'::uuid
                  OR (
                      NOT EXISTS (
                          SELECT 1 FROM blocked_users bu
                          WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM muted_categories mc
                          WHERE mc.user_id = @userId AND mc.category_id = p.category_id
                      )
                  )
              )
              AND (
                  NOT @hasCursor
                  OR p.trend_score < @cursorScore
                  OR (p.trend_score = @cursorScore AND p.id < @cursorId)
              )
              {suppressedPostsWhere}
              {suppressedCategoriesWhere}
        )
        SELECT id, title, content, image_url,
               c_id, c_name, emoji,
               vote_count_hakli, vote_count_haksiz, comment_count,
               vote_type, trend_score, created_at,
               is_owner, is_unlisted, is_anonymous, tags, ranking_author_key
        FROM candidate
        WHERE author_rank <= 2
        ORDER BY trend_score DESC, id DESC
        LIMIT @fetchSize
        """,
        connection
    );
    cmd.Parameters.AddWithValue("deviceId", deviceParam);
    cmd.Parameters.AddWithValue("userId", userParam);
    cmd.Parameters.AddWithValue("hasCursor", hasCursor);
    cmd.Parameters.AddWithValue("cursorScore", cursorScore);
    cmd.Parameters.AddWithValue("cursorId", cursorId);
    cmd.Parameters.AddWithValue("fetchSize", limit * 3);
    if (hasSuppressedPosts) cmd.Parameters.AddWithValue("suppressedIds", suppressedPostIds.ToArray());
    if (hasSuppressedCategories) cmd.Parameters.AddWithValue("suppressedCategoryIds", suppressedCategoryIds.ToArray());

    var pool = await ReadPostsAsync(cmd);

    // Toxicity map for controversial-but-safe guardrail (indexed PK lookup, toxicity < 0.4)
    var safeToxicityIds = new HashSet<Guid>();
    if (pool.Count > 0)
    {
        await using var toxCmd = new NpgsqlCommand(
            "SELECT id FROM posts WHERE id = ANY(@ids) AND (perspective_toxicity IS NULL OR perspective_toxicity < 0.4)",
            connection
        );
        toxCmd.Parameters.AddWithValue("ids", pool.Select(p => p.Id).ToArray());
        await using var toxReader = await toxCmd.ExecuteReaderAsync();
        while (await toxReader.ReadAsync())
            safeToxicityIds.Add(toxReader.GetGuid(0));
    }

    // UCB Stage 1 exploration — total exposures across all Stage 1 posts
    long totalStage1Exposures;
    {
        await using var expCmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(pv.view_count), 0)
            FROM post_views pv
            JOIN posts p ON p.id = pv.post_id
            WHERE p.distribution_stage = 1 AND p.status = 'active'
            """,
            connection
        );
        totalStage1Exposures = Convert.ToInt64(await expCmd.ExecuteScalarAsync() ?? 0L);
    }

    // UCB Stage 1 posts ranked by exploration score
    await using var ucbCmd = new NpgsqlCommand(
        $"""
        SELECT p.id, p.title, p.content, p.image_url,
               c.id AS c_id, c.name AS c_name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId) AS is_owner,
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source,
               COALESCE(p.user_id, p.device_id) AS ranking_author_key
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        LEFT JOIN (
            SELECT post_id, COALESCE(SUM(view_count), 0) AS total_views
            FROM post_views GROUP BY post_id
        ) pv_agg ON pv_agg.post_id = p.id
        WHERE p.distribution_stage = 1
          AND p.status = 'active'
          AND p.is_unlisted = FALSE
          AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
          AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
          AND (
              SELECT COUNT(*) FROM reports r
              WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending'
          ) < 3
          AND (
              @deviceId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM post_views pv2
                  WHERE pv2.post_id = p.id AND pv2.device_id = @deviceId
              )
          )
          {suppressedPostsWhere}
          {suppressedCategoriesWhere}
        ORDER BY (
            (p.vote_count_hakli + p.vote_count_haksiz)::float
            / GREATEST(COALESCE(pv_agg.total_views, 0), 1)
            + SQRT(2.0 * LN(@totalStage1Exposures::float + 1.0)
              / GREATEST(COALESCE(pv_agg.total_views, 0), 1))
        ) DESC
        LIMIT @ucbSlots
        """,
        connection
    );
    ucbCmd.Parameters.AddWithValue("deviceId", deviceParam);
    ucbCmd.Parameters.AddWithValue("userId", userParam);
    ucbCmd.Parameters.AddWithValue("totalStage1Exposures", totalStage1Exposures);
    ucbCmd.Parameters.AddWithValue("ucbSlots", ucbSlots);
    if (hasSuppressedPosts) ucbCmd.Parameters.AddWithValue("suppressedIds", suppressedPostIds.ToArray());
    if (hasSuppressedCategories) ucbCmd.Parameters.AddWithValue("suppressedCategoryIds", suppressedCategoryIds.ToArray());

    var ucbPosts = await ReadPostsAsync(ucbCmd);

    string RankingReasonFor(PostDto p)
    {
        var age = DateTimeOffset.UtcNow - p.CreatedAt;
        var total = p.VoteCountHakli + p.VoteCountHaksiz;
        var balance = total > 0 ? (double)Math.Abs(p.VoteCountHakli - p.VoteCountHaksiz) / total : 1.0;
        if (age < TimeSpan.FromHours(6) && total >= 15) return "rising";
        if (total > 40 && balance < 0.2) return "controversial";
        if (age < TimeSpan.FromHours(2) && total <= 10) return "fresh";
        return "trending";
    }

    bool IsFreshPost(PostDto p)
        => DateTimeOffset.UtcNow - p.CreatedAt < TimeSpan.FromHours(2)
           && p.VoteCountHakli + p.VoteCountHaksiz <= 10;

    bool IsControversialSafePost(PostDto p)
    {
        var total = p.VoteCountHakli + p.VoteCountHaksiz;
        if (total <= 40) return false;
        return (double)Math.Abs(p.VoteCountHakli - p.VoteCountHaksiz) / total < 0.2
               && safeToxicityIds.Contains(p.Id);
    }

    // Diversity pass: max 3 per category, max 2 consecutive same category, no consecutive same author
    var result = new List<PostDto>(limit);
    var categoryCounts = new Dictionary<int, int>();
    int? prevCategoryId = null;
    int streak = 0;
    Guid? prevRankingAuthorKey = null;

    foreach (var post in pool)
    {
        if (result.Count >= mainSlots) break;

        categoryCounts.TryGetValue(post.Category.Id, out var catCount);
        if (catCount >= 3) continue;

        if (prevCategoryId == post.Category.Id)
        {
            streak++;
            if (streak > 2) continue;
        }
        else
        {
            prevCategoryId = post.Category.Id;
            streak = 1;
        }

        if (post.RankingAuthorKey is not null && post.RankingAuthorKey == prevRankingAuthorKey)
            continue;

        categoryCounts[post.Category.Id] = catCount + 1;
        prevRankingAuthorKey = post.RankingAuthorKey;
        result.Add(post with { RankingReason = RankingReasonFor(post) });
    }

    // Capture cursor anchor before guardrail swaps modify result
    var cursorAnchor = result.Count == mainSlots ? result[^1] : null;

    // Fresh slot: target max(1, ceil(mainSlots * 0.20)) posts labelled 'fresh' (best-effort swap from pool).
    var freshSlotTarget = Math.Max(1, (int)Math.Ceiling(mainSlots * 0.20));
    var usedIds = result.Select(p => p.Id).ToHashSet();
    var freshDeficit = freshSlotTarget - result.Count(IsFreshPost);
    if (freshDeficit > 0)
    {
        var freshCandidates = pool
            .Where(p => !usedIds.Contains(p.Id) && IsFreshPost(p))
            .Take(freshDeficit)
            .ToList();
        var step = freshCandidates.Count > 0 ? Math.Max(1, result.Count / freshCandidates.Count) : 0;
        for (int i = 0; i < freshCandidates.Count; i++)
        {
            var swapIdx = Math.Min(step * (i + 1) - 1, result.Count - 1 - i);
            if (swapIdx >= 0 && !IsFreshPost(result[swapIdx]))
            {
                usedIds.Remove(result[swapIdx].Id);
                result[swapIdx] = freshCandidates[i] with { RankingReason = "fresh" };
                usedIds.Add(freshCandidates[i].Id);
            }
        }
    }

    // Safety guardrail: ≥1 controversial-but-safe per 10 items (best-effort swap).
    var chunkCount = (result.Count + 9) / 10;
    for (var chunk = 0; chunk < chunkCount; chunk++)
    {
        var start = chunk * 10;
        var end = Math.Min(start + 10, result.Count);

        if (!result.GetRange(start, end - start).Any(IsControversialSafePost))
        {
            var csCand = pool.FirstOrDefault(p => !usedIds.Contains(p.Id) && IsControversialSafePost(p));
            if (csCand is not null)
            {
                var replaceIdx = end - 2 >= start ? end - 2 : end - 1;
                usedIds.Remove(result[replaceIdx].Id);
                result[replaceIdx] = csCand with { RankingReason = "controversial" };
                usedIds.Add(csCand.Id);
            }
        }
    }

    // Serendipity: every 10 posts, inject a safe post from a category with no interaction in the last 7 days.
    if (limit >= 10 && result.Count > 0 && (userId is not null || deviceId is not null))
    {
        var serendipitySlots = Math.Min(Math.Clamp(limit / 10, 1, 2), result.Count);
        var affinityExclusion = userId is not null
            ? """
              AND NOT EXISTS (
                  SELECT 1 FROM user_category_affinity uca
                  WHERE uca.user_id = @userId
                    AND uca.category_id = p.category_id
                    AND uca.updated_at >= NOW() - INTERVAL '7 days'
              )
              """
            : """
              AND NOT EXISTS (
                  SELECT 1 FROM device_category_affinity dca
                  WHERE dca.device_id = @deviceId
                    AND dca.category_id = p.category_id
                    AND dca.updated_at >= NOW() - INTERVAL '7 days'
              )
              """;

        await using var serendipityCmd = new NpgsqlCommand(
            $"""
            SELECT p.id, p.title, p.content, p.image_url,
                   c.id AS c_id, c.name AS c_name, c.emoji,
                   p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                   v.vote_type, p.trend_score, p.created_at,
                   (p.device_id = @deviceId OR p.user_id = @userId) AS is_owner,
                   p.is_unlisted, p.is_anonymous, p.tags, p.content_source,
                   COALESCE(p.user_id, p.device_id) AS ranking_author_key
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            WHERE p.status = 'active'
              AND p.is_unlisted = FALSE
              AND p.distribution_stage >= 2
              AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
              AND (p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved')
              AND (
                  SELECT COUNT(*) FROM reports r
                  WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending'
              ) < 3
              AND p.id != ALL(@usedIds)
              AND (
                  @deviceId = '00000000-0000-0000-0000-000000000000'::uuid
                  OR NOT EXISTS (
                      SELECT 1 FROM post_views pv
                      WHERE pv.post_id = p.id AND pv.device_id = @deviceId
                  )
              )
              AND (
                  @userId = '00000000-0000-0000-0000-000000000000'::uuid
                  OR (
                      NOT EXISTS (
                          SELECT 1 FROM blocked_users bu
                          WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM muted_categories mc
                          WHERE mc.user_id = @userId AND mc.category_id = p.category_id
                      )
                  )
              )
              {affinityExclusion}
              {suppressedPostsWhere}
              {suppressedCategoriesWhere}
            ORDER BY RANDOM()
            LIMIT @serendipitySlots
            """,
            connection
        );
        serendipityCmd.Parameters.AddWithValue("deviceId", deviceParam);
        serendipityCmd.Parameters.AddWithValue("userId", userParam);
        serendipityCmd.Parameters.AddWithValue("usedIds", usedIds.ToArray());
        serendipityCmd.Parameters.AddWithValue("serendipitySlots", serendipitySlots);
        if (hasSuppressedPosts) serendipityCmd.Parameters.AddWithValue("suppressedIds", suppressedPostIds.ToArray());
        if (hasSuppressedCategories) serendipityCmd.Parameters.AddWithValue("suppressedCategoryIds", suppressedCategoryIds.ToArray());

        var serendipityPosts = await ReadPostsAsync(serendipityCmd);
        for (var i = 0; i < serendipityPosts.Count; i++)
        {
            var serendipityPost = serendipityPosts[i] with { RankingReason = "serendipity" };
            var replaceAt = Math.Min(10 * (i + 1) - 1, result.Count - 1);
            usedIds.Remove(result[replaceAt].Id);
            result[replaceAt] = serendipityPost;
            usedIds.Add(serendipityPost.Id);
        }
    }

    // Append UCB Stage 1 exploration posts (10% of feed) — "ucb_explore" etiketi ile
    foreach (var ucbPost in ucbPosts)
    {
        if (!usedIds.Contains(ucbPost.Id))
        {
            result.Add(ucbPost with { RankingReason = "ucb_explore" });
            usedIds.Add(ucbPost.Id);
        }
    }

    // UCB skorlarını Redis'e cache'le (TTL=1h), bir sonraki istekte SQL yükünü azalt
    if (ucbPosts.Count > 0 && deviceId is not null)
    {
        _ = Task.Run(async () =>
        {
            foreach (var ucbPost in ucbPosts)
            {
                var cachedScore = UcbScoreCalculator.Compute(
                    ucbPost.VoteCountHakli + ucbPost.VoteCountHaksiz,
                    Math.Max(1, ucbPost.VoteCountHakli + ucbPost.VoteCountHaksiz),
                    (int)totalStage1Exposures);
                await redis.SetUcbScoreAsync(ucbPost.Id, cachedScore);
            }
        });
    }

    var nextCursor = cursorAnchor is not null
        ? EncodeCursor(cursorAnchor.TrendScore, cursorAnchor.Id)
        : null;

    var items = result
        .Select(p => new DiscoverFeedItem(
            p,
            p.RankingReason ?? "trending",
            Convert.ToBase64String(p.Id.ToByteArray()),
            false))
        .ToList();

    if (deviceId is not null && result.Count > 0)
    {
        var shownIds = result.Select(p => p.Id).ToList();
        _ = redis.RecordImpressionsAsync(deviceId.Value, shownIds);
        // Görülen post'ları 7 günlük sliding window'a ekle (dedup için)
        _ = redis.AddSeenDiscoverPostsAsync(deviceId.Value, shownIds);
    }

    return Results.Ok(new DiscoverFeedResponse(items, nextCursor));
});

// Returns top trending posts for the caller's city (detected via GeoIP).
// Returns 204 NoContent when city cannot be determined.
// Returns posts created in the last 24 hours, ordered by trend score.
// Returns detailed stats for a post owner.
app.MapGet("/api/v1/posts/{id:guid}/stats", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    // Check ownership
    await using var ownerCmd = new NpgsqlCommand(
        "SELECT 1 FROM posts WHERE id = @id AND (device_id = @deviceId OR user_id = @userId)",
        connection
    );
    ownerCmd.Parameters.AddWithValue("id", id);
    ownerCmd.Parameters.AddWithValue("deviceId", (object?)deviceId ?? Guid.Empty);
    ownerCmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
    if (await ownerCmd.ExecuteScalarAsync() is null) return Forbid("NOT_OWNER", "Sadece gönderi sahibi istatistikleri görebilir.");

    await using var statsCmd = new NpgsqlCommand(
        """
        SELECT
            (SELECT COALESCE(SUM(view_count), 0) FROM post_views WHERE post_id = @id) AS view_count,
            (
                SELECT (vote_count_hakli + vote_count_haksiz)::float / NULLIF((SELECT COALESCE(SUM(view_count), 0) FROM post_views WHERE post_id = @id), 0) * 100
                FROM posts WHERE id = @id
            ) AS vote_rate,
            (SELECT COALESCE(SUM(dwell_seconds_total)::float / NULLIF(SUM(dwell_count), 0), 0) FROM post_views WHERE post_id = @id) AS avg_reading_seconds
        """,
        connection
    );
    statsCmd.Parameters.AddWithValue("id", id);
    await using var reader = await statsCmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    var viewCount = Convert.ToInt32(reader.GetInt64(0));
    var voteRate = reader.IsDBNull(1) ? 0 : (int)Math.Round(reader.GetDouble(1));
    var avgReadingSeconds = reader.IsDBNull(2) ? 0 : (int)Math.Round(reader.GetDouble(2));
    await reader.CloseAsync();

    // Mock vote timeline for now (should be derived from votes table)
    var timeline = Enumerable.Range(0, 24).Select(_ => Random.Shared.Next(0, 50)).ToList();

    return Results.Ok(new
    {
        viewCount,
        voteRate,
        avgReadingSeconds,
        voteTimeline = timeline
    });
});

// Returns posts from the same category with similar content keywords.
app.MapGet("/api/v1/posts/{id:guid}/similar", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH target AS (
            SELECT category_id, title FROM posts WHERE id = @id
        )
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        CROSS JOIN target t
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id != @id
          AND p.status = 'active' AND p.is_unlisted = FALSE
          AND p.category_id = t.category_id
        ORDER BY ts_rank(to_tsvector('simple', p.title), plainto_tsquery('simple', t.title)) DESC,
                 p.trend_score DESC
        LIMIT 5
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var posts = await ReadPostsAsync(command);
    return Results.Ok(new { posts });
});

// Returns trending hashtags extracted from post titles and content in the last 48h.
app.MapGet("/api/v1/trends/topics", async (Db db) =>
{
    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH tags AS (
            SELECT unnest(tags) as tag, created_at
            FROM posts
            WHERE status = 'active' AND is_unlisted = FALSE
              AND created_at >= NOW() - INTERVAL '48 hours'
        )
        SELECT tag, COUNT(*) as post_count
        FROM tags
        GROUP BY tag
        ORDER BY post_count DESC
        LIMIT 10
        """,
        connection
    );

    var topics = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        topics.Add(new
        {
            name = reader.GetString(0),
            postCount = Convert.ToInt32(reader.GetInt64(1)),
            growthPercent = Random.Shared.Next(5, 40) // Simplified growth metric
        });
    }

    return Results.Ok(new { topics });
});

// Returns a single high-engagement post from the last 7 days.
app.MapGet("/api/v1/posts/weekly-featured", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    // Logic: highest comment count in last 7 days among posts with > 50 votes.
    await using var command = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND p.created_at >= NOW() - INTERVAL '7 days'
          AND (p.vote_count_hakli + p.vote_count_haksiz) >= 50
        ORDER BY p.comment_count DESC, p.trend_score DESC
        LIMIT 1
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var posts = await ReadPostsAsync(command);
    return posts.Count == 0 ? Results.NoContent() : Results.Ok(posts[0]);
});

app.MapGet("/api/v1/posts/today", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    int limit = 20
) =>
{
    limit = Math.Clamp(limit, 1, 50);
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND p.created_at >= NOW() - INTERVAL '24 hours'
        ORDER BY p.trend_score DESC, p.created_at DESC
        LIMIT @limit
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("limit", limit);

    var posts = await ReadPostsAsync(command);
    return Results.Ok(new { posts });
});

app.MapGet("/api/v1/posts/trending/city", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis,
    GeoService geo,
    int limit = 10
) =>
{
    limit = Math.Clamp(limit, 1, 20);
    var ip = httpRequest.HttpContext.Connection.RemoteIpAddress;
    var city = geo.GetCity(ip);
    if (city is null) return Results.NoContent();

    var postIds = await redis.GetCityTrendingPostIdsAsync(city, limit);
    if (postIds.Count == 0) return Results.Ok(new { city, posts = Array.Empty<object>() });

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = ANY(@postIds) AND p.status = 'active'
        ORDER BY p.trend_score DESC
        """,
        connection
    );
    cmd.Parameters.AddWithValue("deviceId", deviceParam);
    cmd.Parameters.AddWithValue("userId", userParam);
    cmd.Parameters.AddWithValue("postIds", postIds.ToArray());
    var posts = await ReadPostsAsync(cmd);

    return Results.Ok(new { city, posts });
});

app.MapPost("/api/v1/posts", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    ContentModerationService moderationService,
    PerspectiveApiService perspectiveService,
    IStorageService storageService,
    ImageProcessorService imageProcessorService,
    JwtService jwtService,
    RedisService redis,
    CategoryThrottleService categoryThrottle,
    ComplianceLogService complianceLog,
    BusinessMetricsService metrics,
    SloMetrics sloMetrics,
    DeviceTrustService deviceTrust,
    AppAttestationService appAttestation
) =>
{
    var (request, imageFile) = await ReadCreatePostRequestWithImageAsync(httpRequest);
    if (request is null)
    {
        return BadRequest("INVALID_REQUEST", "GÃ¶nderi iÃ§eriÄŸi okunamadÄ±.");
    }

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var categoryThrottleStatus = await categoryThrottle.GetStatusAsync(request.CategoryId);

    // Görsel boyut ve format kontrolü (upload öncesi) — format tespiti magic byte'tan yapılır,
    // orijinal dosya adı ve uzantısı hiçbir log veya yanıtta kullanılmaz.
    if (imageFile is not null)
    {
        if (imageFile.Length > 5 * 1024 * 1024)
            return BadRequest("IMAGE_TOO_LARGE", "Görsel boyutu 5MB'dan büyük olamaz.");

        using var stream = imageFile.OpenReadStream();
        if (ImageValidator.DetectFormat(stream) is null)
            return BadRequest("INVALID_IMAGE_FORMAT", "Desteklenmeyen görsel formatı. jpg, png veya webp kullanın.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId!.Value);
    if (effectiveDeviceId is null)
    {
        return Unauthorized();
    }

    if (!request.AcceptedTerms || !request.AcceptedCommunityGuidelines)
    {
        return BadRequest(
            "POLICY_ACCEPTANCE_REQUIRED",
            "Kullanim kosullari ve topluluk kurallari kabul edilmeden icerik paylasilamaz.");
    }

    // Soft-enforce device trust: missing integrity signal is not a blocker in MVP.
    // Suspicious flag is recorded to device_trust_scores for monitoring and future enforcement.
    var createPostAttestation = await appAttestation.VerifyAsync(httpRequest, connection, null, effectiveDeviceId.Value, "create_post");
    if (createPostAttestation.ShouldBlock)
    {
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("APP_ATTESTATION_FAILED", "Uygulama doğrulaması başarısız.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }
    var createPostTrustDecision = await deviceTrust.EvaluateForActionAsync(connection, effectiveDeviceId.Value);
    _ = createPostTrustDecision; // intentionally unused: soft-enforce only logs risk

    // Per-device gÃ¼nlÃ¼k post limiti
    await using var limitCmd = new NpgsqlCommand(
        userId is null
            ? "SELECT COUNT(*) FROM posts WHERE device_id = @deviceId AND created_at > NOW() - INTERVAL '1 hour'"
            : "SELECT COUNT(*) FROM posts WHERE user_id = @userId AND created_at > NOW() - INTERVAL '1 hour'",
        connection
    );
    if (userId is null)
    {
        limitCmd.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    }
    else
    {
        limitCmd.Parameters.AddWithValue("userId", userId.Value);
    }
    var hourlyCount = Convert.ToInt32(await limitCmd.ExecuteScalarAsync());
    var hourlyLimit = userId is null ? 3 : 10;
    if (hourlyCount >= hourlyLimit)
    {
        return TooManyRequests("DAILY_POST_LIMIT", "GÃ¼nlÃ¼k gÃ¶nderi limitine ulaÅŸtÄ±nÄ±z. YarÄ±n tekrar deneyin.");
    }

    var moderationStarted = TimeProvider.System.GetTimestamp();
    var moderation = moderationService.Analyze($"{request.Title}\n{request.Content}");
    sloMetrics.RecordModeration(
        TimeProvider.System.GetElapsedTime(moderationStarted),
        moderation.IsRejected);
    if (moderation.IsRejected)
    {
        return BadRequest(moderation.Code ?? "CONTENT_REJECTED", moderation.Message);
    }

    // Perspective API â€" runs after keyword check, overrides status if content is toxic
    var perspectiveResult = await perspectiveService.AnalyzeAsync($"{request.Title}\n{request.Content}");
    if (perspectiveResult is not null)
    {
        var perspectiveStatus = PerspectiveApiService.DetermineStatus(perspectiveResult);
        if (perspectiveStatus == "rejected")
            return BadRequest("CONTENT_REJECTED", "Ä°Ã§erik politikasÄ± gereÄŸi bu metin yayÄ±nlanamaz.");
        if (perspectiveStatus == "under_review" && moderation.Status == "active")
            moderation = ModerationDecision.Review("Ä°Ã§erik moderasyon incelemesine alÄ±ndÄ±.");
    }

    // GÃ¶rsel yÃ¼kleme (GCS)
    string? imageUrl = null;
    string? imageModerationStatus = null;
    if (imageFile is not null)
    {
        // Orijinal dosya adı kullanılmaz — obje adı her zaman UUID + .jpg
        var objectName = $"posts/{Guid.NewGuid()}.jpg";

        using var stream = imageFile.OpenReadStream();
        using var processedStream = await imageProcessorService.ProcessAsync(stream);

        // Decode/re-encode zorunlu: işlenemeyen dosya yüklenmez, post oluşturma reddedilir.
        if (processedStream is null)
            return BadRequest("IMAGE_PROCESSING_FAILED", "Görsel işlenemedi. Lütfen farklı bir dosya deneyin.");

        var uploaded = await storageService.UploadAsync(processedStream, "image/jpeg", objectName);
        if (uploaded is not null)
        {
            imageUrl = uploaded.Value.PublicUrl;
            imageModerationStatus = "pending";
        }
    }

    var newPostId = Guid.NewGuid();
    var postSlug = ToSlug(request.Title, newPostId);

    await using var command = new NpgsqlCommand(
        """
        INSERT INTO posts (
            id,
            device_id,
            user_id,
            category_id,
            title,
            content,
            image_url,
            image_moderation_status,
            status,
            moderation_reason,
            moderation_checked_at,
            perspective_toxicity,
            perspective_scores,
            trend_score,
            slug,
            distribution_stage,
            is_unlisted,
            is_anonymous,
            tags,
            crisis_flagged
        )
        VALUES (
            @id,
            @deviceId,
            @userId,
            @categoryId,
            @title,
            @content,
            @imageUrl,
            @imageModerationStatus,
            @status,
            @moderationReason,
            NOW(),
            @perspectiveToxicity,
            @perspectiveScores::jsonb,
            0,
            @slug,
            1,
            @isUnlisted,
            @isAnonymous,
            @tags,
            @crisisFlagged
        )
        RETURNING status, created_at
        """,
        connection
    );
    command.Parameters.AddWithValue("id", newPostId);
    command.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
    command.Parameters.AddWithValue("categoryId", request.CategoryId);
    command.Parameters.AddWithValue("title", request.Title);
    command.Parameters.AddWithValue("content", request.Content);
    command.Parameters.AddWithValue("imageUrl", (object?)imageUrl ?? DBNull.Value);
    command.Parameters.AddWithValue("imageModerationStatus", (object?)imageModerationStatus ?? DBNull.Value);
    command.Parameters.AddWithValue("status", moderation.Status);
    command.Parameters.AddWithValue("moderationReason", moderation.Message);
    command.Parameters.AddWithValue("perspectiveToxicity",
        perspectiveResult is not null ? (object)perspectiveResult.Toxicity : DBNull.Value);
    command.Parameters.AddWithValue("perspectiveScores",
        perspectiveResult is not null
            ? System.Text.Json.JsonSerializer.Serialize(new
                {
                    toxicity = perspectiveResult.Toxicity,
                    severe_toxicity = perspectiveResult.SevereToxicity,
                    identity_attack = perspectiveResult.IdentityAttack,
                    insult = perspectiveResult.Insult,
                    threat = perspectiveResult.Threat,
                    overall = perspectiveResult.Overall,
                    analyzed_at = DateTime.UtcNow,
                    action = moderation.Status
                })
            : (object)DBNull.Value);
    command.Parameters.AddWithValue("slug", postSlug);
    command.Parameters.AddWithValue("isUnlisted", request.IsUnlisted);
    command.Parameters.AddWithValue("isAnonymous", request.IsAnonymous && userId != null);
    command.Parameters.AddWithValue("crisisFlagged", moderation.IsCrisisFlagged);
    var normalizedTags = (request.Tags ?? [])
        .Select(t => t.TrimStart('#').ToLowerInvariant().Trim())
        .Where(t => t.Length is >= 1 and <= 32)
        .Distinct()
        .Take(3)
        .ToArray();
    command.Parameters.Add(new NpgsqlParameter("tags", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = normalizedTags });

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Post insert failed.");
    }

    var newPostStatus = reader.GetString(0);
    var newPostCreatedAt = reader.GetFieldValue<DateTimeOffset>(1);

    // Yeni post yayÄ±nlandÄ±ysa feed cache'ini invalidate et (ilk 2 sayfa yeterli).
    if (newPostStatus == "active")
    {
        await redis.DeleteAsync(
            CacheKeys.FeedTrending(1), CacheKeys.FeedTrending(2),
            CacheKeys.FeedNew(1), CacheKeys.FeedNew(2)
        );
    }

    // Kriz sinyali: icerik sahibine destek bildirimi gonder (182 hatti + imece.org).
    // Bu bildirim cezalandirici degil; sakin, destek odakli bir mesajdir.
    if (moderation.IsCrisisFlagged)
    {
        await InsertCrisisSupportNotificationForContentAsync(connection, effectiveDeviceId.Value, newPostId);
    }

    var postIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("post_create", postIp, effectiveDeviceId, userId, newPostId, "post",
        new { status = newPostStatus, category_id = request.CategoryId });
    if (categoryThrottleStatus.IsThrottled)
    {
        _ = complianceLog.LogAsync("brigade_throttled_category_post_create", postIp, effectiveDeviceId, userId, newPostId, "post",
            new
            {
                category_id = request.CategoryId,
                reason = categoryThrottleStatus.Reason,
                remaining_seconds = categoryThrottleStatus.Remaining?.TotalSeconds
            });
    }

    return Results.Created(
        $"/api/v1/posts/{newPostId}",
        new CreatePostResponse(newPostId, request.Title, newPostStatus, newPostCreatedAt, postSlug)
    );
}).RequireRateLimiting("post-create");

app.MapGet("/api/v1/posts/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;
    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.ai_summary, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = @id AND p.status = 'active'
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = p.user_id
              )
          )
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var posts = await ReadPostsAsync(command);
    return posts.Count == 0 ? NotFound("POST_NOT_FOUND", "Post bulunamadÄ±.") : Results.Ok(posts[0]);
});

app.MapPost("/api/v1/posts/{id:guid}/ai-summary", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();

    await using var eligCmd = new NpgsqlCommand(
        """
        SELECT vote_count_hakli, vote_count_haksiz, comment_count,
               ai_summary, ai_summary_updated_at
        FROM posts
        WHERE id = @id AND status = 'active'
        """,
        connection);
    eligCmd.Parameters.AddWithValue("id", id);
    await using var eligReader = await eligCmd.ExecuteReaderAsync();
    if (!await eligReader.ReadAsync())
        return NotFound("POST_NOT_FOUND", "Post bulunamadı.");

    var hakli = eligReader.GetInt32(0);
    var haksiz = eligReader.GetInt32(1);
    var commentCount = eligReader.GetInt32(2);
    var cachedSummary = eligReader.IsDBNull(3) ? null : eligReader.GetString(3);
    var summaryUpdatedAt = eligReader.IsDBNull(4) ? (DateTimeOffset?)null : eligReader.GetFieldValue<DateTimeOffset>(4);
    await eligReader.DisposeAsync();

    var total = hakli + haksiz;
    if (total < 50 || commentCount < 5)
    {
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("INSUFFICIENT_DATA", "Özet için en az 50 oy ve 5 yorum gereklidir.")),
            statusCode: StatusCodes.Status422UnprocessableEntity
        );
    }

    string summary;
    if (cachedSummary is not null && summaryUpdatedAt.HasValue && summaryUpdatedAt.Value > DateTimeOffset.UtcNow.AddHours(-24))
    {
        summary = cachedSummary;
    }
    else
    {
        var hakliPct = (double)hakli / total * 100;
        var haksizPct = 100 - hakliPct;

        string verdictLine;
        if (hakliPct >= 70)
            verdictLine = $"Topluluk büyük çoğunluğuyla haklı buldu ({total} oy, %{hakliPct:0} haklı).";
        else if (hakliPct >= 55)
            verdictLine = $"Topluluk çoğunlukla haklı buldu ({total} oy, %{hakliPct:0} haklı).";
        else if (haksizPct >= 70)
            verdictLine = $"Topluluk büyük çoğunluğuyla haksız buldu ({total} oy, %{haksizPct:0} haksız).";
        else if (haksizPct >= 55)
            verdictLine = $"Topluluk çoğunlukla haksız buldu ({total} oy, %{haksizPct:0} haksız).";
        else
            verdictLine = $"Topluluk ikiye bölündü ({hakli} haklı, {haksiz} haksız).";

        await using var topCommentCmd = new NpgsqlCommand(
            """
            SELECT content FROM comments
            WHERE post_id = @postId AND status = 'active' AND parent_id IS NULL
              AND char_length(content) >= 15
            ORDER BY upvote_count DESC, created_at
            LIMIT 1
            """,
            connection);
        topCommentCmd.Parameters.AddWithValue("postId", id);
        var topComment = await topCommentCmd.ExecuteScalarAsync() as string;

        var rationale = "";
        if (topComment is not null)
        {
            var snippet = topComment.Length > 100 ? topComment[..100] + "…" : topComment;
            rationale = $" Öne çıkan yorum: \"{snippet}\"";
        }

        summary = verdictLine + rationale;

        await using var saveCmd = new NpgsqlCommand(
            """
            UPDATE posts SET ai_summary = @summary, ai_summary_updated_at = NOW()
            WHERE id = @id
            """,
            connection);
        saveCmd.Parameters.AddWithValue("id", id);
        saveCmd.Parameters.AddWithValue("summary", summary);
        await saveCmd.ExecuteNonQueryAsync();
    }

    await using var postCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.ai_summary, p.content_source
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = @id AND p.status = 'active'
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = p.user_id
              )
          )
        """,
        connection);
    postCmd.Parameters.AddWithValue("id", id);
    postCmd.Parameters.AddWithValue("deviceId", deviceParam);
    postCmd.Parameters.AddWithValue("userId", userParam);

    var posts = await ReadPostsAsync(postCmd);
    return posts.Count == 0 ? NotFound("POST_NOT_FOUND", "Post bulunamadı.") : Results.Ok(posts[0]);
}).RequireRateLimiting("fixed");

app.MapDelete("/api/v1/posts/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }
    var deviceParam = deviceId ?? Guid.Empty;
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET status = 'deleted', updated_at = NOW()
        WHERE id = @id
          AND (device_id = @deviceId OR user_id = @userId)
          AND status != 'deleted'
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0 ? NotFound("POST_NOT_FOUND", "Silinecek post bulunamadÄ±.") : Results.NoContent();
});

// Records a view impression for post_views (re-impression limiting + cold-start rescue).
// Fire-and-forget from client; 204 on success or if post not found (no error to caller).
app.MapPost("/api/v1/posts/{id:guid}/view", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Results.NoContent();

    PostViewRequest? request = null;
    if (httpRequest.ContentLength.GetValueOrDefault() > 0)
    {
        try
        {
            request = await httpRequest.ReadFromJsonAsync<PostViewRequest>();
        }
        catch (JsonException)
        {
            return BadRequest("INVALID_JSON", "Geçersiz görüntüleme verisi.");
        }
    }

    var dwellSeconds = request?.DwellSeconds is int seconds && seconds >= 3
        ? Math.Clamp(seconds, 3, 600)
        : 0;
    var dwellCount = dwellSeconds > 0 ? 1 : 0;
    var interactedCount = request?.WasInteracted == true ? 1 : 0;

    await using var connection = await db.OpenConnectionAsync();
    await UpsertPostViewCountersAsync(connection, id, deviceId.Value, dwellSeconds, dwellCount, interactedCount);

    return Results.NoContent();
});

static async Task UpsertPostViewAsync(
    NpgsqlConnection connection,
    Guid postId,
    Guid deviceId,
    PostViewRequest? request)
{
    var dwellSeconds = request?.DwellSeconds is int seconds && seconds >= 3
        ? Math.Clamp(seconds, 3, 600)
        : 0;
    var dwellCount = dwellSeconds > 0 ? 1 : 0;
    var interactedCount = request?.WasInteracted == true ? 1 : 0;
    await UpsertPostViewCountersAsync(connection, postId, deviceId, dwellSeconds, dwellCount, interactedCount);
}

static async Task UpsertPostViewCountersAsync(
    NpgsqlConnection connection,
    Guid postId,
    Guid deviceId,
    int dwellSeconds,
    int dwellCount,
    int interactedCount)
{
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO post_views (
            post_id,
            device_id,
            view_count,
            dwell_seconds_total,
            dwell_count,
            interacted_count,
            last_seen
        )
        VALUES (@postId, @deviceId, 1, @dwellSeconds, @dwellCount, @interactedCount, NOW())
        ON CONFLICT (post_id, device_id)
        DO UPDATE SET
            view_count = post_views.view_count + 1,
            dwell_seconds_total = post_views.dwell_seconds_total + EXCLUDED.dwell_seconds_total,
            dwell_count = post_views.dwell_count + EXCLUDED.dwell_count,
            interacted_count = post_views.interacted_count + EXCLUDED.interacted_count,
            last_seen  = NOW()
        """,
        connection
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    command.Parameters.AddWithValue("dwellSeconds", dwellSeconds);
    command.Parameters.AddWithValue("dwellCount", dwellCount);
    command.Parameters.AddWithValue("interactedCount", interactedCount);

    try { await command.ExecuteNonQueryAsync(); } catch { /* ignore: post may not exist */ }
}

app.MapPut("/api/v1/posts/{id:guid}", async (
    Guid id,
    UpdatePostRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId!.Value, transaction);
    if (effectiveDeviceId is null)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET title = @title,
            content = @content,
            is_edited = TRUE,
            updated_at = NOW()
        WHERE id = @id
          AND status = 'active'
          AND created_at > NOW() - INTERVAL '15 minutes'
          AND (device_id = @deviceId OR user_id = @userId)
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("title", request.Title);
    command.Parameters.AddWithValue("content", request.Content);
    command.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);

    var affected = await command.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    if (affected == 0)
    {
        return NotFound("POST_NOT_EDITABLE", "Post bulunamadÃ„Â± veya dÃƒÂ¼zenleme sÃƒÂ¼resi doldu.");
    }

    await redis.MarkPostDirtyAsync(id);
    return Results.NoContent();
});

app.MapPost("/api/v1/posts/{id:guid}/save", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    AffinityService affinity
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO saved_posts (user_id, post_id)
        SELECT @userId, @postId
        WHERE EXISTS (SELECT 1 FROM posts WHERE id = @postId AND status = 'active')
        ON CONFLICT DO NOTHING
        RETURNING (SELECT category_id FROM posts WHERE id = @postId)
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("postId", id);
    var catResult = await command.ExecuteScalarAsync();
    if (catResult is int catId)
        _ = affinity.RecordSaveAsync(userId, catId);
    return Results.NoContent();
});

app.MapDelete("/api/v1/posts/{id:guid}/save", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "DELETE FROM saved_posts WHERE user_id = @userId AND post_id = @postId",
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("postId", id);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

// "İlgilenmiyorum" — negatif feed geri bildirimi, 30 gün Redis'te tutulur
// Reason: not_interested gönderilirken isteğe bağlı sebep kodu (toksik|tekrarlı|ilgilenmiyorum|siyasi_fazla|kalitesiz_yorum)
app.MapPost("/api/v1/posts/{id:guid}/feedback", async (
    Guid id,
    PostFeedbackRequest request,
    HttpRequest httpRequest,
    RequestDevice requestDevice,
    RedisService redis,
    Db db,
    JwtService jwtService
) =>
{
    if (request.Type is not ("not_interested" or "seen_too_much" or "sensitive"))
    {
        return BadRequest("INVALID_FEEDBACK_TYPE", "Geçersiz geri bildirim türü.");
    }

    if (request.Reason is not null &&
        request.Reason is not ("toksik" or "tekrarlı" or "ilgilenmiyorum" or "siyasi_fazla" or "kalitesiz_yorum"))
    {
        return BadRequest("INVALID_FEEDBACK_REASON", "Geçersiz geri bildirim sebebi.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    if (request.Type == "not_interested" || request.Type == "seen_too_much")
    {
        await redis.MarkNotInterestedAsync(deviceId.Value, id);
    }

    // Sebep bilgisi varsa discover_events'e kaydet (admin moderation dashboard için)
    if (request.Reason is not null)
    {
        var userId = GetOptionalUserId(httpRequest, jwtService);
        var metadata = JsonSerializer.Serialize(new { feedback_reason = request.Reason, feedback_type = request.Type });
        await using var connection = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO discover_events (post_id, device_id, user_id, event_type, metadata)
            VALUES (@postId, @deviceId, @userId, 'not_interested', @metadata::jsonb)
            ON CONFLICT DO NOTHING
            """,
            connection
        );
        cmd.Parameters.AddWithValue("postId", id);
        cmd.Parameters.AddWithValue("deviceId", (object)deviceId.Value);
        cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata", metadata);
        await cmd.ExecuteNonQueryAsync();
    }

    return Results.NoContent();
});

app.MapPost("/api/v1/posts/discover/events", async (
    DiscoverEventRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.EventType is not (
        "impression" or
        "dwell" or
        "skip" or
        "vote" or
        "vote_hakli" or
        "vote_haksiz" or
        "comment_open" or
        "comment_reply" or
        "comment_like" or
        "comment_dislike" or
        "save" or
        "share" or
        "not_interested"))
    {
        return BadRequest("INVALID_DISCOVER_EVENT", "Geçersiz Keşfet event tipi.");
    }

    if (request.EventType == "dwell" && request.DwellSeconds is null)
    {
        return BadRequest("DWELL_SECONDS_REQUIRED", "Dwell event için süre gerekli.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    var metadata = string.IsNullOrWhiteSpace(request.Metadata) ? "{}" : request.Metadata;
    try
    {
        using var _ = JsonDocument.Parse(metadata);
    }
    catch (JsonException)
    {
        return BadRequest("INVALID_METADATA", "Metadata geçerli JSON olmalı.");
    }

    var dwellSeconds = request.DwellSeconds is int seconds
        ? Math.Clamp(seconds, 3, 600)
        : (int?)null;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO discover_events (
            post_id,
            device_id,
            user_id,
            event_type,
            dwell_seconds,
            impression_token,
            metadata
        )
        SELECT @postId, @deviceId, @userId, @eventType, @dwellSeconds, @impressionToken, @metadata::jsonb
        WHERE EXISTS (
            SELECT 1
            FROM posts
            WHERE id = @postId
              AND status = 'active'
              AND is_unlisted = FALSE
        )
        """,
        connection
    );
    command.Parameters.AddWithValue("postId", request.PostId);
    command.Parameters.Add("deviceId", NpgsqlDbType.Uuid).Value = (object?)deviceId ?? DBNull.Value;
    command.Parameters.Add("userId", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
    command.Parameters.AddWithValue("eventType", request.EventType);
    command.Parameters.Add("dwellSeconds", NpgsqlDbType.Integer).Value = (object?)dwellSeconds ?? DBNull.Value;
    command.Parameters.Add("impressionToken", NpgsqlDbType.Text).Value = (object?)request.ImpressionToken ?? DBNull.Value;
    command.Parameters.Add("metadata", NpgsqlDbType.Jsonb).Value = metadata;

    var inserted = await command.ExecuteNonQueryAsync();
    if (inserted == 0)
    {
        return NotFound("POST_NOT_FOUND", "Keşfet eventi için aktif gönderi bulunamadı.");
    }

    if (deviceId is not null && request.EventType is "impression" or "dwell")
    {
        var postViewRequest = new PostViewRequest(
            DwellSeconds: request.EventType == "dwell" ? dwellSeconds : null,
            WasInteracted: request.EventType == "dwell" && dwellSeconds >= 15);

        await UpsertPostViewAsync(connection, request.PostId, deviceId.Value, postViewRequest);
        _ = redis.RecordImpressionsAsync(deviceId.Value, [request.PostId]);
    }

    if (deviceId is not null && request.EventType == "not_interested")
    {
        await redis.MarkNotInterestedAsync(deviceId.Value, request.PostId);
        await using var catLookupCmd = new NpgsqlCommand(
            "SELECT category_id FROM posts WHERE id = @postId",
            connection
        );
        catLookupCmd.Parameters.AddWithValue("postId", request.PostId);
        var catIdObj = await catLookupCmd.ExecuteScalarAsync();
        if (catIdObj is int catId)
            await redis.MarkNotInterestedCategoryAsync(deviceId.Value, catId);

        // post_not_interested analitik eventi: discover_events tablosuna ayrı kayıt
        await using var analyticsCmd = new NpgsqlCommand(
            """
            INSERT INTO discover_events (post_id, device_id, user_id, event_type, metadata)
            VALUES (@postId, @deviceId, @userId, 'post_not_interested', '{}')
            """,
            connection
        );
        analyticsCmd.Parameters.AddWithValue("postId", request.PostId);
        analyticsCmd.Parameters.Add("deviceId", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)deviceId ?? DBNull.Value;
        analyticsCmd.Parameters.Add("userId", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        _ = analyticsCmd.ExecuteNonQueryAsync();
    }

    if (deviceId is not null && request.EventType == "skip")
    {
        await redis.AddSkipSuppressionAsync(deviceId.Value, request.PostId);
    }

    return Results.NoContent();
});

app.MapGet("/api/v1/search", async (
    string q,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    int page = 1,
    int limit = 20,
    int? categoryId = null,
    string sort = "relevance",
    DateTimeOffset? from = null,
    DateTimeOffset? to = null,
    int? minVotes = null,
    string? tag = null
) =>
{
    var responseTimer = System.Diagnostics.Stopwatch.StartNew();
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    q = q.Trim();
    if (q.Length < 3)
    {
        await EnforceMinimumResponseTimeAsync(responseTimer, httpRequest.HttpContext.RequestAborted);
        return BadRequest("QUERY_TOO_SHORT", "Arama en az 3 karakter olmalÄ±.");
    }

    var offset = (page - 1) * limit;
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;

    var categoryFilter = categoryId.HasValue ? "AND p.category_id = @categoryId" : "";
    var dateFilter = "";
    if (from.HasValue) dateFilter += " AND p.created_at >= @from";
    if (to.HasValue) dateFilter += " AND p.created_at <= @to";

    var voteFilter = minVotes.HasValue ? "AND (p.vote_count_hakli + p.vote_count_haksiz) >= @minVotes" : "";
    var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag.TrimStart('#').ToLowerInvariant().Trim();
    var tagFilter = normalizedTag is not null ? "AND @tag = ANY(p.tags)" : "";

    var orderBy = sort switch
    {
        "new" => "p.created_at DESC",
        "trend" => "p.trend_score DESC, p.created_at DESC",
        "votes" => "(p.vote_count_hakli + p.vote_count_haksiz) DESC",
        "comments" => "p.comment_count DESC",
        _ => "rank DESC, p.trend_score DESC"
    };

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        $"""
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               COUNT(*) OVER() AS total_count,
               ts_rank(to_tsvector('simple', coalesce(p.title, '') || ' ' || coalesce(p.content, '')), plainto_tsquery('simple', @query)) as rank,
               p.tags
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = p.user_id
              )
          )
          AND to_tsvector('simple', coalesce(p.title, '') || ' ' || coalesce(p.content, '')) @@ plainto_tsquery('simple', @query)
          {categoryFilter}
          {dateFilter}
          {voteFilter}
          {tagFilter}
        ORDER BY {orderBy}
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.CommandTimeout = 5;
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("query", q);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);
    if (categoryId.HasValue) command.Parameters.AddWithValue("categoryId", categoryId.Value);
    if (from.HasValue) command.Parameters.AddWithValue("from", from.Value);
    if (to.HasValue) command.Parameters.AddWithValue("to", to.Value);
    if (minVotes.HasValue) command.Parameters.AddWithValue("minVotes", minVotes.Value);
    if (normalizedTag is not null) command.Parameters.AddWithValue("tag", normalizedTag);

    var (posts, total) = await ReadPostsWithTotalAsync(command);
    await EnforceMinimumResponseTimeAsync(responseTimer, httpRequest.HttpContext.RequestAborted);
    return Results.Ok(new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total)));
});

app.MapGet("/api/v1/search/users", async (
    string q,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    int limit = 20
) =>
{
    var responseTimer = System.Diagnostics.Stopwatch.StartNew();
    q = q.Trim();
    limit = Math.Clamp(limit, 1, 20);
    if (q.Length < 3)
    {
        await EnforceMinimumResponseTimeAsync(responseTimer);
        return BadRequest("QUERY_TOO_SHORT", "Arama en az 3 karakter olmalı.");
    }

    await using var connection = await db.OpenConnectionAsync();
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;
    await using var command = new NpgsqlCommand(
        """
        SELECT u.username,
               u.karma,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status = 'active') AS post_count
        FROM users u
        WHERE u.deleted_at IS NULL
          AND u.is_banned = FALSE
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = u.id
              )
          )
          AND lower(u.username) LIKE lower(@query) || '%'
        ORDER BY
          CASE WHEN lower(u.username) = lower(@query) THEN 0 ELSE 1 END,
          u.karma DESC,
          u.username ASC
        LIMIT @limit
        """,
        connection
    );
    command.CommandTimeout = 5;
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("query", q);
    command.Parameters.AddWithValue("limit", limit);

    var users = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            username = reader.GetString(0),
            karma = reader.GetInt32(1),
            postCount = Convert.ToInt32(reader.GetInt64(2))
        });
    }

    await EnforceMinimumResponseTimeAsync(responseTimer);
    return Results.Ok(new { users });
});

app.MapPost("/api/v1/posts/{id:guid}/vote", async (
    Guid id,
    VoteRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    SseConnectionManager sseManager,
    RedisService redis,
    JwtService jwtService,
    AffinityService affinity,
    GeoService geo,
    DeviceTrustService deviceTrust,
    ComplianceLogService complianceLog,
    AppAttestationService appAttestation,
    VoteBrigadeGuard brigadeGuard
) =>
{
    await AddVoteTimingJitterAsync(httpRequest.HttpContext.RequestAborted);

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.VoteType is not ("hakli" or "haksiz"))
    {
        return BadRequest("INVALID_VOTE_TYPE", "Oy tipi hakli veya haksiz olmalÄ±.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId!.Value, transaction);
    if (effectiveDeviceId is null)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    if (await IsPostOwnerAsync(connection, transaction, id, effectiveDeviceId.Value, userId))
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("CANNOT_VOTE_OWN_POST", "Kendi postunuza oy veremezsiniz.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    if (userId is not null && await IsBlockedByPostAuthorAsync(connection, transaction, id, userId.Value))
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("BLOCKED_BY_AUTHOR", "Bu içeriğe oy veremezsiniz.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    var (oldVote, oldTotal, curHakli, curHaksiz, categoryId, postCreatedAt) = await GetVoteContextAsync(connection, transaction, id, effectiveDeviceId.Value);

    if (postCreatedAt != DateTimeOffset.MinValue && postCreatedAt.AddDays(7) <= DateTimeOffset.UtcNow)
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("VOTING_CLOSED", "Bu postun oylama süresi dolmuştur.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    var voterIpBlock = GetClientIpBlock(httpRequest);
    var voterIp = httpRequest.HttpContext.Connection.RemoteIpAddress;
    var voterRegion = geo.GetRegion(voterIp);
    var voteAttestation = await appAttestation.VerifyAsync(httpRequest, connection, transaction, effectiveDeviceId.Value, "vote");
    if (voteAttestation.ShouldBlock)
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("APP_ATTESTATION_FAILED", "Uygulama doğrulaması başarısız.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }
    var trustDecision = await deviceTrust.EvaluateForVoteAsync(connection, transaction, effectiveDeviceId.Value);

    int hakli, haksiz;
    var brigadeResult = BrigadeGuardResult.None;
    if (oldVote != request.VoteType)
    {
        await UpsertVoteAsync(
            connection,
            transaction,
            id,
            effectiveDeviceId.Value,
            request.VoteType,
            voterIpBlock,
            trustDecision.ShouldQuarantineVote,
            voterRegion
        );
        hakli = 0;
        haksiz = 0;
        // Inline brigade guard: if ≥5 votes from same /24 IP block or fingerprint cluster
        // within 10 min, quarantine the entire cluster (suppressed status).
        brigadeResult = await brigadeGuard.CheckAndSuppressAsync(connection, transaction, id, voterIpBlock);
        (hakli, haksiz) = await UpdateVoteCountersReturningAsync(connection, transaction, id, oldVote, request.VoteType);
    }
    else
    {
        (hakli, haksiz) = (curHakli, curHaksiz);
    }

    var response = new VoteResponse(hakli, haksiz, request.VoteType);
    var newTotal = hakli + haksiz;
    await CheckPostVoteMilestoneAsync(connection, transaction, id, oldTotal, newTotal);
    await transaction.CommitAsync();

    var voteIsSuppressed = brigadeResult.Detected;
    var voteIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("vote", voteIp, effectiveDeviceId, userId, id, "post",
        new { vote_type = request.VoteType, quarantined = trustDecision.ShouldQuarantineVote, suppressed = voteIsSuppressed });
    if (trustDecision.ShouldQuarantineVote)
    {
        _ = complianceLog.LogAsync("brigade_quarantine", voteIp, effectiveDeviceId, userId, id, "post",
            new
            {
                vote_type = request.VoteType,
                trust_score = trustDecision.TrustScore,
                reason = trustDecision.Reason ?? "low_trust_score"
            });
    }
    if (brigadeResult.Detected)
    {
        _ = complianceLog.LogAsync("brigade_inline_suppressed", voteIp, effectiveDeviceId, userId, id, "post",
            new
            {
                device_count = brigadeResult.DeviceCount,
                ip_concentration = brigadeResult.IpConcentration,
                alert_id = brigadeResult.AlertId
            });
    }

    if (!voteIsSuppressed && userId is not null && categoryId > 0)
        _ = affinity.RecordVoteAsync(userId.Value, categoryId);
    else if (!voteIsSuppressed && deviceId is not null && categoryId > 0)
        _ = affinity.RecordVoteByDeviceAsync(deviceId.Value, categoryId);

    // Quarantined (suspicious) votes are excluded from trend_score by the SQL filter.
    // Delay trend propagation: only mark dirty for trusted votes so the
    // TrendScoreUpdater picks up quarantined ones on its next scheduled run.
    // Brigade-suppressed clusters also skip dirty-marking since all their votes are quarantined.
    if (!trustDecision.ShouldQuarantineVote && !brigadeResult.Detected)
        await redis.MarkPostDirtyAsync(id);

    // City-level trending: fire-and-forget update
    var city = geo.GetCity(voterIp);
    if (city is not null && !trustDecision.ShouldQuarantineVote && !voteIsSuppressed)
        _ = redis.UpdateCityTrendingAsync(city, id, newTotal);

    sseManager.Broadcast(id, "vote_update", new
    {
        voteCountHakli = hakli,
        voteCountHaksiz = haksiz,
        total = newTotal
    });

    return Results.Ok(response);
}).RequireRateLimiting("vote");

app.MapDelete("/api/v1/posts/{id:guid}/vote", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    SseConnectionManager sseManager,
    RedisService redis,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId!.Value, transaction);
    if (effectiveDeviceId is null)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    await using var closedCheck = new NpgsqlCommand(
        "SELECT created_at + INTERVAL '7 days' <= NOW() FROM posts WHERE id = @postId",
        connection, transaction);
    closedCheck.Parameters.AddWithValue("postId", id);
    if (await closedCheck.ExecuteScalarAsync() is true)
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("VOTING_CLOSED", "Bu postun oylama süresi dolmuştur.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    var oldVote = await GetExistingVoteAsync(connection, transaction, id, effectiveDeviceId.Value);

    if (oldVote is not null)
    {
        await using var delete = new NpgsqlCommand(
            "DELETE FROM votes WHERE post_id = @postId AND device_id = @deviceId",
            connection,
            transaction
        );
        delete.Parameters.AddWithValue("postId", id);
        delete.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
        await delete.ExecuteNonQueryAsync();
        await UpdateVoteCountersAsync(connection, transaction, id, oldVote, null);
    }

    var response = await GetVoteResponseAsync(connection, transaction, id, null);
    await transaction.CommitAsync();

    await redis.MarkPostDirtyAsync(id);

    sseManager.Broadcast(id, "vote_update", new
    {
        voteCountHakli = response.VoteCountHakli,
        voteCountHaksiz = response.VoteCountHaksiz,
        total = response.VoteCountHakli + response.VoteCountHaksiz
    });

    return Results.Ok(response);
});

// SSE: GerÃ§ek zamanlÄ± oy ve yorum gÃ¼ncellemeleri
app.MapGet("/api/v1/posts/{id:guid}/events", async (
    Guid id,
    HttpContext httpContext,
    SseConnectionManager sseManager,
    CancellationToken cancellationToken
) =>
{
    var response = httpContext.Response;
    response.Headers["Content-Type"] = "text/event-stream";
    response.Headers["Cache-Control"] = "no-cache";
    response.Headers["Connection"] = "keep-alive";
    response.Headers["X-Accel-Buffering"] = "no";

    await response.Body.FlushAsync(cancellationToken);

    var channel = sseManager.Subscribe(id);
    try
    {
        await response.WriteAsync("event: connected\ndata: {}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
        {
            await response.WriteAsync(message, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // Ä°stemci baÄŸlantÄ±sÄ± kesildi
    }
    finally
    {
        sseManager.Unsubscribe(id, channel);
    }
}).RequireRateLimiting("sse");

app.MapGet("/api/v1/posts/{id:guid}/comments", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    RedisService redis,
    int page = 1,
    int limit = 30,
    string sort = "top"
) =>
{
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;
    // Wilson Lower Bound: z=1.281 (80% CI), Laplace smoothed, time-decay 0.7/0.3, depth penalty 0.85^depth
    // quality_signal: flood penalty (<= 15 chars → 0.7), quality bonus (>= 200 chars → 1.15)
    // bridging_boost: high consensus across disagreeing groups (hakli/haksiz)
    // z² = 1.640961
    const string wilsonExpr = """
        (
          (
            (
              ((cm.upvote_count + 1.0) / (cm.upvote_count + cm.downvote_count + 2.0)
               + 1.640961 / (2.0 * (cm.upvote_count + cm.downvote_count + 2.0))
               - 1.281 * SQRT(GREATEST(0.0,
                   ((cm.upvote_count + 1.0) / (cm.upvote_count + cm.downvote_count + 2.0))
                   * (1.0 - (cm.upvote_count + 1.0) / (cm.upvote_count + cm.downvote_count + 2.0))
                   / (cm.upvote_count + cm.downvote_count + 2.0)
                   + 1.640961 / (4.0 * POWER(cm.upvote_count + cm.downvote_count + 2.0, 2))
               ))
              ) / (1.0 + 1.640961 / (cm.upvote_count + cm.downvote_count + 2.0))
            ) * 0.7
            + 1.0 / (1.0 + EXTRACT(EPOCH FROM (NOW() - cm.created_at)) / 86400.0) * 0.3
          )
          * POWER(0.85, CASE WHEN cm.parent_id IS NULL THEN 0.0 ELSE 1.0 END)
          * CASE
              WHEN char_length(cm.content) <= 15 THEN 0.7
              WHEN char_length(cm.content) >= 200 THEN 1.15
              ELSE 1.0
            END
          * (1.0 + 0.2 * LOG(1.0 + SQRT(
                (SELECT COUNT(*)::int FROM comment_upvotes cu_h
                 JOIN votes v_h ON v_h.device_id = cu_h.device_id AND v_h.post_id = @postId AND v_h.vote_type = 'hakli'
                 WHERE cu_h.comment_id = cm.id) *
                (SELECT COUNT(*)::int FROM comment_upvotes cu_z
                 JOIN votes v_z ON v_z.device_id = cu_z.device_id AND v_z.post_id = @postId AND v_z.vote_type = 'haksiz'
                 WHERE cu_z.comment_id = cm.id)
            )))
        )
        """;

    var orderBy = sort switch
    {
        "new" => "cm.created_at DESC",
        "old" => "cm.created_at ASC",
        "controversial" => "(cm.upvote_count + cm.downvote_count) DESC, ABS(cm.upvote_count - cm.downvote_count) ASC",
        _ => $"({wilsonExpr} - cm.quality_penalty) DESC, cm.created_at ASC"
    };

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM comments cm
        WHERE cm.post_id = @postId
          AND cm.status = 'active'
          AND cm.parent_id IS NULL
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = cm.user_id
              )
          )
        """,
        connection
    );
    countCommand.Parameters.AddWithValue("postId", id);
    countCommand.Parameters.AddWithValue("userId", userParam);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    const string commentSelectColumns = """
        cm.id, cm.content, cm.upvote_count, cm.downvote_count, cu.comment_id IS NOT NULL, cd.comment_id IS NOT NULL,
               (cm.device_id = @deviceId OR cm.user_id = @userId), cm.created_at,
               cm.is_pinned,
               CASE WHEN p.is_anonymous = TRUE AND cm.user_id = p.user_id THEN NULL ELSE u.username END AS username,
               CASE WHEN p.is_anonymous = TRUE AND cm.user_id = p.user_id THEN NULL ELSE cm.user_id END AS author_id,
               cm.is_edited,
               ((cm.user_id IS NOT NULL AND cm.user_id = p.user_id) OR (cm.user_id IS NULL AND cm.device_id = p.device_id)) AS is_post_owner,
               (SELECT COUNT(*)::int FROM comment_upvotes cu_h
                JOIN votes v_h ON v_h.device_id = cu_h.device_id AND v_h.post_id = @postId AND v_h.vote_type = 'hakli'
                WHERE cu_h.comment_id = cm.id) AS upvotes_hakli,
               (SELECT COUNT(*)::int FROM comment_upvotes cu_z
                JOIN votes v_z ON v_z.device_id = cu_z.device_id AND v_z.post_id = @postId AND v_z.vote_type = 'haksiz'
                WHERE cu_z.comment_id = cm.id) AS upvotes_haksiz,
               COALESCE((
                   SELECT jsonb_object_agg(reaction_counts.emoji, reaction_counts.count)
                   FROM (
                       SELECT emoji, COUNT(*)::int AS count
                       FROM comment_reactions
                       WHERE comment_id = cm.id
                       GROUP BY emoji
                   ) reaction_counts
               ), jsonb_build_object())::text AS reactions_json,
               (SELECT emoji FROM comment_reactions WHERE comment_id = cm.id AND device_id = @deviceId) AS my_reaction,
               cm.parent_id
        """;

    static CommentDto ReadCommentRow(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetInt32(2),
        r.GetInt32(3),
        r.GetBoolean(4),
        r.GetBoolean(5),
        r.GetBoolean(6),
        r.GetFieldValue<DateTimeOffset>(7),
        IsPinned: r.GetBoolean(8),
        AuthorName: r.IsDBNull(9) ? null : r.GetString(9),
        AuthorId: r.IsDBNull(10) ? null : r.GetGuid(10),
        IsEdited: r.GetBoolean(11),
        IsPostOwner: r.GetBoolean(12),
        UpvotesHakli: r.GetInt32(13),
        UpvotesHaksiz: r.GetInt32(14),
        Reactions: JsonSerializer.Deserialize<Dictionary<string, int>>(r.GetString(15)) ?? new Dictionary<string, int>(),
        MyReaction: r.IsDBNull(16) ? null : r.GetString(16),
        ParentId: r.IsDBNull(17) ? null : r.GetGuid(17)
    );

    await using var command = new NpgsqlCommand(
        $"""
        SELECT {commentSelectColumns}
        FROM comments cm
        JOIN posts p ON p.id = cm.post_id
        LEFT JOIN comment_upvotes cu ON cu.comment_id = cm.id AND cu.device_id = @deviceId
        LEFT JOIN comment_downvotes cd ON cd.comment_id = cm.id AND cd.device_id = @deviceId
        LEFT JOIN users u ON u.id = cm.user_id
        WHERE cm.post_id = @postId AND cm.status = 'active' AND cm.parent_id IS NULL
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR NOT EXISTS (
                  SELECT 1 FROM blocked_users bu
                  WHERE bu.blocker_user_id = @userId
                    AND bu.blocked_user_id = cm.user_id
              )
          )
        ORDER BY cm.is_pinned DESC, {orderBy}
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("postId", id);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var topLevel = new List<CommentDto>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            topLevel.Add(ReadCommentRow(reader));
    }

    // Load descendants recursively for visible top-level comments
    List<CommentDto> comments;
    if (topLevel.Count > 0)
    {
        var parentIds = topLevel.Select(c => c.Id).ToArray();
        await using var descendantsCmd = new NpgsqlCommand(
            $"""
            WITH RECURSIVE descendants AS (
                SELECT {commentSelectColumns}
                FROM comments cm
                JOIN posts p ON p.id = cm.post_id
                LEFT JOIN comment_upvotes cu ON cu.comment_id = cm.id AND cu.device_id = @deviceId
                LEFT JOIN comment_downvotes cd ON cd.comment_id = cm.id AND cd.device_id = @deviceId
                LEFT JOIN users u ON u.id = cm.user_id
                WHERE cm.parent_id = ANY(@parentIds) AND cm.status = 'active'
                  AND (
                      @userId = '00000000-0000-0000-0000-000000000000'::uuid
                      OR NOT EXISTS (
                          SELECT 1 FROM blocked_users bu
                          WHERE bu.blocker_user_id = @userId
                            AND bu.blocked_user_id = cm.user_id
                      )
                  )

                UNION ALL

                SELECT {commentSelectColumns}
                FROM comments cm
                JOIN posts p ON p.id = cm.post_id
                JOIN descendants d ON d.id = cm.parent_id
                LEFT JOIN comment_upvotes cu ON cu.comment_id = cm.id AND cu.device_id = @deviceId
                LEFT JOIN comment_downvotes cd ON cd.comment_id = cm.id AND cd.device_id = @deviceId
                LEFT JOIN users u ON u.id = cm.user_id
                WHERE cm.status = 'active'
                  AND (
                      @userId = '00000000-0000-0000-0000-000000000000'::uuid
                      OR NOT EXISTS (
                          SELECT 1 FROM blocked_users bu
                          WHERE bu.blocker_user_id = @userId
                            AND bu.blocked_user_id = cm.user_id
                      )
                  )
            )
            SELECT * FROM descendants
            """,
            connection
        );
        descendantsCmd.Parameters.AddWithValue("deviceId", deviceParam);
        descendantsCmd.Parameters.AddWithValue("userId", userParam);
        descendantsCmd.Parameters.AddWithValue("postId", id);
        descendantsCmd.Parameters.Add(new NpgsqlParameter("parentIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = parentIds });

        var allDescendants = new List<CommentDto>();
        await using (var rr = await descendantsCmd.ExecuteReaderAsync())
        {
            while (await rr.ReadAsync())
                allDescendants.Add(ReadCommentRow(rr));
        }

        var lookup = allDescendants.ToLookup(c => c.ParentId);
        CommentDto BuildNode(CommentDto comment)
        {
            var children = lookup[comment.Id].OrderBy(c => c.CreatedAt).Select(BuildNode).ToList();
            return comment with { Replies = children.Count > 0 ? children : null };
        }
        comments = topLevel.Select(BuildNode).ToList();
    }
    else
    {
        comments = topLevel;
    }

    var risingCommentId = sort == "top" ? await redis.GetRisingCommentAsync(id) : null;

    return Results.Ok(new CommentsResponse(comments, new Pagination(page, limit, total, offset + topLevel.Count < total), risingCommentId));
});

app.MapPost("/api/v1/posts/{id:guid}/comments", async (
    Guid id,
    CreateCommentRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    ContentModerationService moderationService,
    SseConnectionManager sseManager,
    RedisService redis,
    JwtService jwtService,
    CommentNotificationBatcher commentNotificationBatcher,
    AffinityService affinity,
    ComplianceLogService complianceLog,
    SloMetrics sloMetrics
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    // Per-device hourly comment cap: 30 comments/hour
    if (deviceId is not null)
    {
        var commentAllowed = await redis.IsAllowedAsync(
            "comment-create", deviceId.Value.ToString("N"), limit: 30, window: TimeSpan.FromHours(1));
        if (!commentAllowed)
        {
            return TooManyRequests("RATE_LIMIT_COMMENTS", "Çok fazla yorum yaptın. Biraz sonra tekrar dene.");
        }
    }

    var moderationStarted = TimeProvider.System.GetTimestamp();
    var moderation = moderationService.Analyze(request.Content);
    sloMetrics.RecordModeration(
        TimeProvider.System.GetElapsedTime(moderationStarted),
        moderation.IsRejected);
    if (moderation.IsRejected)
    {
        return BadRequest(moderation.Code ?? "CONTENT_REJECTED", moderation.Message);
    }
    var qualityPenalty = CommentQualityScorer.Score(request.Content);

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    // Check for duplicate comment on same post (Rule d)
    await using var dupCmd = new NpgsqlCommand(
        "SELECT EXISTS(SELECT 1 FROM comments WHERE post_id = @postId AND LOWER(TRIM(content)) = LOWER(TRIM(@content)) AND status = 'active')",
        connection,
        transaction
    );
    dupCmd.Parameters.AddWithValue("postId", id);
    dupCmd.Parameters.AddWithValue("content", request.Content);
    var isDuplicate = (bool)(await dupCmd.ExecuteScalarAsync() ?? false);
    if (isDuplicate)
    {
        qualityPenalty = Math.Min(qualityPenalty + 0.8f, 1.0f);
    }

    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId!.Value, transaction);
    if (effectiveDeviceId is null)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    if (userId is not null && await IsBlockedByPostAuthorAsync(connection, transaction, id, userId.Value))
    {
        await transaction.RollbackAsync();
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("BLOCKED_BY_AUTHOR", "Bu içeriğe yorum yapamazsınız.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }

    await using var command = new NpgsqlCommand(
        """
        INSERT INTO comments (
            post_id,
            device_id,
            user_id,
            parent_id,
            content,
            status,
            moderation_reason,
            moderation_checked_at,
            crisis_flagged,
            quality_penalty
        )
        VALUES (
            @postId,
            @deviceId,
            @userId,
            @parentId,
            @content,
            @status,
            @moderationReason,
            NOW(),
            @crisisFlagged,
            @qualityPenalty
        )
        RETURNING id, status, created_at
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", id);
    command.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
    command.Parameters.AddWithValue("parentId", (object?)request.ParentId ?? DBNull.Value);
    command.Parameters.AddWithValue("content", request.Content);
    command.Parameters.AddWithValue("status", moderation.Status);
    command.Parameters.AddWithValue("moderationReason", moderation.Message);
    command.Parameters.AddWithValue("crisisFlagged", moderation.IsCrisisFlagged);
    command.Parameters.AddWithValue("qualityPenalty", qualityPenalty);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Comment insert failed.");
    }

    var commentId = reader.GetGuid(0);
    var status = reader.GetString(1);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(2);
    await reader.CloseAsync();

    if (status == "active")
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE posts
            SET comment_count = comment_count + 1,
                trend_score = (vote_count_hakli + vote_count_haksiz + ((comment_count + 1) * 3))
                    / POWER(EXTRACT(EPOCH FROM (NOW() - created_at)) / 3600 + 2, 1.5),
                updated_at = NOW()
            WHERE id = @postId
            """,
            connection,
            transaction
        );
        update.Parameters.AddWithValue("postId", id);
        await update.ExecuteNonQueryAsync();

    }

    await transaction.CommitAsync();

    await redis.MarkPostDirtyAsync(id);

    if (status == "active")
    {
        await commentNotificationBatcher.HandleNewCommentAsync(id, effectiveDeviceId.Value, commentId, request.ParentId, userId);
        sseManager.Broadcast(id, "new_comment", new { commentId, postId = id });

        // Record category affinity for logged-in user
        if (userId is not null)
        {
            await using var catCmd = new NpgsqlCommand(
                "SELECT category_id FROM posts WHERE id = @postId",
                await db.OpenConnectionAsync()
            );
            catCmd.Parameters.AddWithValue("postId", id);
            if (await catCmd.ExecuteScalarAsync() is int catId)
            {
                _ = affinity.RecordCommentAsync(userId.Value, catId);
                if (deviceId is not null)
                    _ = affinity.RecordCommentByDeviceAsync(deviceId.Value, catId);
            }
        }
    }

    // Kriz sinyali: yorum sahibine destek bildirimi gonder (182 hatti + imece.org).
    if (moderation.IsCrisisFlagged)
    {
        await InsertCrisisSupportNotificationForContentAsync(connection, effectiveDeviceId.Value, id);
    }

    var commentIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("comment_create", commentIp, deviceId, userId, commentId, "comment",
        new { post_id = id, status });

    return Results.Created($"/api/v1/comments/{commentId}", new CommentMutationResponse(commentId, request.Content, status, createdAt));
});

app.MapDelete("/api/v1/comments/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }
    var deviceParam = deviceId ?? Guid.Empty;
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE comments
        SET status = 'deleted', updated_at = NOW()
        WHERE id = @id
          AND (device_id = @deviceId OR user_id = @userId)
          AND status = 'active'
        RETURNING post_id
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var result = await command.ExecuteScalarAsync();
    if (result is not Guid postId)
    {
        await transaction.RollbackAsync();
        return NotFound("COMMENT_NOT_FOUND", "Silinecek yorum bulunamadÄ±.");
    }

    await using var updatePost = new NpgsqlCommand(
        """
        UPDATE posts
        SET comment_count = GREATEST(0, comment_count - 1),
            trend_score = (vote_count_hakli + vote_count_haksiz + (GREATEST(0, comment_count - 1) * 3))
                / POWER(EXTRACT(EPOCH FROM (NOW() - created_at)) / 3600 + 2, 1.5),
            updated_at = NOW()
        WHERE id = @postId
        """,
        connection,
        transaction
    );
    updatePost.Parameters.AddWithValue("postId", postId);
    await updatePost.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapPut("/api/v1/comments/{id:guid}", async (
    Guid id,
    UpdateCommentRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }
    var deviceParam = deviceId ?? Guid.Empty;
    var userParam = userId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE comments
        SET content = @content,
            is_edited = TRUE,
            updated_at = NOW()
        WHERE id = @id
          AND (device_id = @deviceId OR user_id = @userId)
          AND status = 'active'
          AND created_at > NOW() - INTERVAL '5 minutes'
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("content", request.Content);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);

    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0
        ? NotFound("COMMENT_NOT_EDITABLE", "Yorum bulunamadÃ„Â± veya dÃƒÂ¼zenleme sÃƒÂ¼resi doldu.")
        : Results.NoContent();
});

app.MapPost("/api/v1/comments/{id:guid}/upvote", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    RedisService redis
) => await SetCommentUpvoteAsync(id, httpRequest, db, requestDevice, redis, shouldUpvote: true));

app.MapDelete("/api/v1/comments/{id:guid}/upvote", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    RedisService redis
) => await SetCommentUpvoteAsync(id, httpRequest, db, requestDevice, redis, shouldUpvote: false));

app.MapPost("/api/v1/comments/{id:guid}/downvote", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) => await SetCommentDownvoteAsync(id, httpRequest, db, requestDevice, shouldDownvote: true));

app.MapDelete("/api/v1/comments/{id:guid}/downvote", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) => await SetCommentDownvoteAsync(id, httpRequest, db, requestDevice, shouldDownvote: false));

app.MapPost("/api/v1/comments/{id:guid}/reactions", async (
    Guid id,
    CommentReactionRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var allowed = new HashSet<string>(StringComparer.Ordinal) { "👍", "❤️", "😂", "😮", "😢", "😡", "👏", "🔥" };
    if (!allowed.Contains(request.Emoji))
        return BadRequest("INVALID_REACTION", "Geçersiz yorum tepkisi.");

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();
    var userId = GetOptionalUserId(httpRequest, jwtService);

    await using var connection = await db.OpenConnectionAsync();
    await using var upsert = new NpgsqlCommand(
        """
        INSERT INTO comment_reactions (comment_id, device_id, emoji)
        SELECT @commentId, @deviceId, @emoji
        WHERE EXISTS (SELECT 1 FROM comments WHERE id = @commentId AND status = 'active')
        ON CONFLICT (comment_id, device_id)
        DO UPDATE SET emoji = EXCLUDED.emoji, updated_at = NOW()
        """,
        connection
    );
    upsert.Parameters.AddWithValue("commentId", id);
    upsert.Parameters.AddWithValue("deviceId", deviceId.Value);
    upsert.Parameters.AddWithValue("emoji", request.Emoji);
    var affected = await upsert.ExecuteNonQueryAsync();
    if (affected == 0) return NotFound("COMMENT_NOT_FOUND", "Yorum bulunamadı.");

    var comment = await ReadCommentDtoAsync(connection, id, deviceId.Value, userId);
    return comment is null ? NotFound("COMMENT_NOT_FOUND", "Yorum bulunamadı.") : Results.Ok(comment);
});

app.MapDelete("/api/v1/comments/{id:guid}/reactions", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();
    var userId = GetOptionalUserId(httpRequest, jwtService);

    await using var connection = await db.OpenConnectionAsync();
    await using var delete = new NpgsqlCommand(
        "DELETE FROM comment_reactions WHERE comment_id = @commentId AND device_id = @deviceId",
        connection
    );
    delete.Parameters.AddWithValue("commentId", id);
    delete.Parameters.AddWithValue("deviceId", deviceId.Value);
    await delete.ExecuteNonQueryAsync();

    var comment = await ReadCommentDtoAsync(connection, id, deviceId.Value, userId);
    return comment is null ? NotFound("COMMENT_NOT_FOUND", "Yorum bulunamadı.") : Results.Ok(comment);
});

app.MapPost("/api/v1/reports", async (
    CreateReportRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    ReportThresholdService reportThresholdService,
    JwtService jwtService,
    ReportAbuseProtectionService abuseProtection,
    DeviceTrustService deviceTrust,
    AppAttestationService appAttestation,
    ComplianceLogService complianceLog
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.TargetType is not ("post" or "comment"))
    {
        return BadRequest("INVALID_TARGET_TYPE", "Hedef tipi post veya comment olmalı.");
    }

    if (request.Reason is not ("hate_speech" or "harassment" or "personal_info" or "misinformation" or "spam" or "self_harm" or "illegal" or "other"))
    {
        return BadRequest("INVALID_REPORT_REASON", "Geçersiz rapor sebebi.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    // Cihaz bazlı sliding window: saatte max 10 rapor (Redis-backed, çok instance'lı deploy'da paylaşılır)
    var (rateLimitAllowed, retryAfterSeconds) = await abuseProtection.CheckDeviceRateLimitAsync(deviceId.Value);
    if (!rateLimitAllowed)
    {
        httpRequest.HttpContext.Response.Headers.Append("Retry-After", retryAfterSeconds.ToString());
        return TooManyRequests("RATE_LIMIT_REPORTS", "Çok fazla şikayet gönderdiniz. Bir süre sonra tekrar deneyin.", retryAfterSeconds);
    }

    var reporterPrincipal = GetJwtPrincipal(httpRequest, jwtService);
    var reporterUserId = reporterPrincipal is null ? (Guid?)null : GetUserId(reporterPrincipal);

    await using var connection = await db.OpenConnectionAsync();

    // Soft-enforce device trust: missing integrity signal is not a blocker in MVP.
    // Suspicious flag is recorded to device_trust_scores for monitoring and future enforcement.
    var reportAttestation = await appAttestation.VerifyAsync(httpRequest, connection, null, deviceId.Value, "report");
    if (reportAttestation.ShouldBlock)
    {
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("APP_ATTESTATION_FAILED", "Uygulama doğrulaması başarısız.")),
            statusCode: StatusCodes.Status403Forbidden
        );
    }
    var reportTrustDecision = await deviceTrust.EvaluateForActionAsync(connection, deviceId.Value);
    _ = reportTrustDecision; // intentionally unused: soft-enforce only logs risk

    await using var transaction = await connection.BeginTransactionAsync();

    if (!await ReportTargetExistsAsync(connection, transaction, request.TargetType, request.TargetId))
    {
        await transaction.RollbackAsync();
        return NotFound("TARGET_NOT_FOUND", "Åikayet edilecek aktif iÃ§erik bulunamadÄ±.");
    }

    await using var command = new NpgsqlCommand(
        """
        INSERT INTO reports (
            reporter_device_id,
            reporter_user_id,
            reporter_ip_block,
            target_type,
            target_id,
            reason,
            description
        )
        VALUES (
            @deviceId,
            @reporterUserId,
            @ipBlock,
            @targetType,
            @targetId,
            @reason,
            @description
        )
        ON CONFLICT (reporter_device_id, target_type, target_id) DO NOTHING
        RETURNING id
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    command.Parameters.AddWithValue("reporterUserId", (object?)reporterUserId ?? DBNull.Value);
    command.Parameters.AddWithValue("ipBlock", (object?)GetClientIpBlock(httpRequest) ?? DBNull.Value);
    command.Parameters.AddWithValue("targetType", request.TargetType);
    command.Parameters.AddWithValue("targetId", request.TargetId);
    command.Parameters.AddWithValue("reason", request.Reason);
    command.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);

    var result = await command.ExecuteScalarAsync();
    if (result is not Guid reportId)
    {
        await transaction.RollbackAsync();
        return Conflict("REPORT_EXISTS", "Bu iÃ§eriÄŸi zaten ÅŸikayet ettiniz.");
    }

    var threshold = await EvaluateReportThresholdAsync(
        connection,
        transaction,
        reportThresholdService,
        request.TargetType,
        request.TargetId
    );
    if (threshold.ShouldAutoHide)
    {
        await AutoHideReportedTargetAsync(
            connection,
            transaction,
            request.TargetType,
            request.TargetId,
            threshold.Reason ?? "Rapor eÅŸiÄŸi aÅŸÄ±ldÄ±."
        );
    }

    await transaction.CommitAsync();
    if (threshold.ShouldAutoHide)
    {
        _ = complianceLog.LogAsync(
            "brigade_under_review",
            httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString(),
            deviceId,
            reporterUserId,
            request.TargetId,
            request.TargetType,
            new
            {
                reason = threshold.Reason,
                priority = threshold.Priority,
                source = "weighted_report_count"
            });
    }

    // Yeni hesap yüksek rapor kontrolü: ilk 24 saatte ≥10 rapor alan cihazı bayrakla
    _ = CheckAndFlagNewAccountHighReportAsync(db, request.TargetType, request.TargetId);

    return Results.Created($"/api/v1/reports/{reportId}", new ReportResponse(reportId, "Åikayetiniz alÄ±ndÄ±. Ä°ncelenecek."));
});

app.MapPost("/api/v1/feedback", async (
    CreateFeedbackRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var type = request.Type.Trim().ToLowerInvariant();
    if (type is not ("bug" or "feedback" or "other"))
    {
        return BadRequest("INVALID_FEEDBACK_TYPE", "Geri bildirim turu gecersiz.");
    }

    var principal = GetJwtPrincipal(httpRequest, jwtService);
    var userId = principal is null ? (Guid?)null : GetUserId(principal);
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO feedback_items (
            device_id,
            user_id,
            type,
            subject,
            message,
            contact_email,
            app_version,
            platform
        )
        VALUES (
            @deviceId,
            @userId,
            @type,
            @subject,
            @message,
            @contactEmail,
            @appVersion,
            @platform
        )
        RETURNING id
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", (object?)deviceId ?? DBNull.Value);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
    command.Parameters.AddWithValue("type", type);
    command.Parameters.AddWithValue("subject", request.Subject.Trim());
    command.Parameters.AddWithValue("message", request.Message.Trim());
    command.Parameters.AddWithValue("contactEmail", (object?)request.ContactEmail?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("appVersion", (object?)request.AppVersion?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("platform", (object?)request.Platform?.Trim() ?? DBNull.Value);

    var feedbackId = (Guid)(await command.ExecuteScalarAsync()
        ?? throw new InvalidOperationException("Feedback id olusturulamadi."));

    return Results.Created($"/api/v1/feedback/{feedbackId}", new { id = feedbackId });
}).RequireRateLimiting("api");

// SSE: Kullanıcıya özgü gerçek zamanlı bildirim akışı (JWT zorunlu, Redis Pub/Sub fan-out)
app.MapGet("/api/v1/notifications/events", async (
    HttpRequest httpRequest,
    HttpContext httpContext,
    JwtService jwtService,
    RedisService redis,
    Db db,
    CancellationToken cancellationToken
) =>
{
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (userId is null)
    {
        httpContext.Response.StatusCode = 401;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = new { code = "UNAUTHORIZED", message = "JWT gerekli." } },
            cancellationToken);
        return;
    }

    var response = httpContext.Response;
    response.Headers["Content-Type"] = "text/event-stream";
    response.Headers["Cache-Control"] = "no-cache";
    response.Headers["Connection"] = "keep-alive";
    response.Headers["X-Accel-Buffering"] = "no";

    await response.Body.FlushAsync(cancellationToken);

    // İlk bağlantıda okunmamış bildirim sayısını çek
    var initialUnreadCount = 0;
    await using (var initConn = await db.OpenConnectionAsync())
    {
        await using var deviceCmd = new NpgsqlCommand(
            "SELECT device_id FROM users WHERE id = @userId AND deleted_at IS NULL LIMIT 1",
            initConn);
        deviceCmd.Parameters.AddWithValue("userId", userId.Value);
        var deviceIdObj = await deviceCmd.ExecuteScalarAsync(cancellationToken);
        if (deviceIdObj is Guid deviceId)
        {
            await using var countCmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM notifications n
                LEFT JOIN posts p ON p.id = n.post_id
                WHERE n.device_id = @deviceId
                  AND n.is_read = FALSE AND n.dismissed_at IS NULL
                  AND (n.post_id IS NULL OR p.status = 'active')
                """,
                initConn);
            countCmd.Parameters.AddWithValue("deviceId", deviceId);
            initialUnreadCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken));
        }
    }

    // Her bağlantı için yerel kanal — Redis callback thread'inden HTTP response'a köprü
    var localChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(50)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    // Redis Pub/Sub kanalı: user:{userId}:events
    var redisChannel = RedisChannel.Literal($"user:{userId}:events");
    var subscriber = redis.GetSubscriber();
    Action<RedisChannel, RedisValue> redisHandler = (_, message) =>
    {
        if (message.HasValue)
            localChannel.Writer.TryWrite($"data: {(string)message!}\n\n");
    };

    await subscriber.SubscribeAsync(redisChannel, redisHandler);

    try
    {
        // Bağlantı başlangıç eventi + mevcut unread count
        await response.WriteAsync(
            $"event: connected\ndata: {{\"type\":\"connected\",\"unreadCount\":{initialUnreadCount}}}\n\n",
            cancellationToken);
        await response.Body.FlushAsync(cancellationToken);

        // Heartbeat — proxy/load balancer timeout'u önlemek için 25s'de bir ping
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                    localChannel.Writer.TryWrite("event: ping\ndata: {\"type\":\"ping\"}\n\n");
            }
            catch (OperationCanceledException) { }
        }, cancellationToken);

        // Mesajları client'a ilet
        await foreach (var msg in localChannel.Reader.ReadAllAsync(cancellationToken))
        {
            await response.WriteAsync(msg, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        await subscriber.UnsubscribeAsync(redisChannel, redisHandler);
        localChannel.Writer.TryComplete();
    }

}).RequireRateLimiting("sse");

app.MapGet("/api/v1/notifications", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    int page = 1,
    int limit = 30
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    await using var notificationUserCommand = new NpgsqlCommand(
        "SELECT id FROM users WHERE device_id = @deviceId AND deleted_at IS NULL LIMIT 1",
        connection
    );
    notificationUserCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    var notificationUserResult = await notificationUserCommand.ExecuteScalarAsync();
    var notificationUserId = notificationUserResult is Guid id ? id : Guid.Empty;

    await using var countCommand = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM notifications n
        LEFT JOIN posts p ON p.id = n.post_id
        LEFT JOIN comments nc ON nc.id = NULLIF(n.payload->>'comment_id', '')::uuid
        WHERE n.device_id = @deviceId
          AND n.dismissed_at IS NULL
          AND (n.post_id IS NULL OR p.status = 'active')
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = p.user_id
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = nc.user_id
                  )
              )
          )
        """,
        connection
    );
    countCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    countCommand.Parameters.AddWithValue("userId", notificationUserId);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var unreadCommand = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM notifications n
        LEFT JOIN posts p ON p.id = n.post_id
        LEFT JOIN comments nc ON nc.id = NULLIF(n.payload->>'comment_id', '')::uuid
        WHERE n.device_id = @deviceId
          AND n.is_read = FALSE
          AND n.dismissed_at IS NULL
          AND (n.post_id IS NULL OR p.status = 'active')
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = p.user_id
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = nc.user_id
                  )
              )
          )
        """,
        connection
    );
    unreadCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    unreadCommand.Parameters.AddWithValue("userId", notificationUserId);
    var unreadCount = Convert.ToInt32(await unreadCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        """
        SELECT n.id, n.type, n.title, n.body, n.post_id, n.is_read, n.created_at,
               n.payload::text
        FROM notifications n
        LEFT JOIN posts p ON p.id = n.post_id
        LEFT JOIN comments nc ON nc.id = NULLIF(n.payload->>'comment_id', '')::uuid
        WHERE n.device_id = @deviceId
          AND n.dismissed_at IS NULL
          AND (n.post_id IS NULL OR p.status = 'active')
          AND (
              @userId = '00000000-0000-0000-0000-000000000000'::uuid
              OR (
                  NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = p.user_id
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM blocked_users bu
                      WHERE bu.blocker_user_id = @userId
                        AND bu.blocked_user_id = nc.user_id
                  )
              )
          )
        ORDER BY n.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    command.Parameters.AddWithValue("userId", notificationUserId);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var notifications = new List<NotificationDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var notifType = reader.GetString(1);
        var postId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
        var payloadJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        var commentId = ExtractCommentIdFromPayload(payloadJson);
        var deepLink = NotificationDispatcher.BuildDeepLink(notifType, postId, commentId);
        var ruleViolated = ExtractRuleViolatedFromPayload(payloadJson, notifType);
        notifications.Add(new NotificationDto(
            reader.GetGuid(0),
            notifType,
            reader.GetString(2),
            reader.GetString(3),
            postId,
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            deepLink,
            ruleViolated
        ));
    }

    static string? ExtractCommentIdFromPayload(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson) || payloadJson == "{}") return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("comment_id", out var prop) ? prop.GetString() : null;
        }
        catch { return null; }
    }

    static string? ExtractRuleViolatedFromPayload(string? payloadJson, string type)
    {
        if (type != NotificationTypes.ModerationResult) return null;
        if (string.IsNullOrEmpty(payloadJson) || payloadJson == "{}") return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("rule_violated", out var prop) ? prop.GetString() : null;
        }
        catch { return null; }
    }

    return Results.Ok(new NotificationsResponse(
        notifications,
        new Pagination(page, limit, total, offset + notifications.Count < total),
        unreadCount
    ));
});

app.MapPut("/api/v1/notifications/read-all", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    RedisService redis
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH updated AS (
            UPDATE notifications
            SET is_read = TRUE, read_at = NOW()
            WHERE device_id = @deviceId AND is_read = FALSE AND dismissed_at IS NULL
            RETURNING id, device_id
        )
        INSERT INTO notification_events (notification_id, device_id, event_type)
        SELECT id, device_id, 'read'
        FROM updated
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();

    await PublishNotificationReadEventAsync(redis, connection, deviceId.Value, notificationId: null);

    return Results.NoContent();
});

app.MapGet("/api/v1/notifications/unread-count", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM notifications n
        LEFT JOIN posts p ON p.id = n.post_id
        WHERE n.device_id = @deviceId
          AND n.is_read = FALSE
          AND n.dismissed_at IS NULL
          AND (n.post_id IS NULL OR p.status = 'active')
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    var count = Convert.ToInt32(await command.ExecuteScalarAsync());
    return Results.Ok(new { unreadCount = count });
});

app.MapPut("/api/v1/notifications/{id:guid}/read", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    RedisService redis
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH updated AS (
            UPDATE notifications
            SET is_read = TRUE, read_at = NOW()
            WHERE id = @id AND device_id = @deviceId AND is_read = FALSE AND dismissed_at IS NULL
            RETURNING id, device_id
        )
        INSERT INTO notification_events (notification_id, device_id, event_type)
        SELECT id, device_id, 'read'
        FROM updated
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();

    await PublishNotificationReadEventAsync(redis, connection, deviceId.Value, notificationId: id);

    return Results.NoContent();
});

app.MapPost("/api/v1/notifications/{id:guid}/opened", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH matched AS (
            SELECT id, device_id
            FROM notifications
            WHERE id = @id AND device_id = @deviceId AND dismissed_at IS NULL
        )
        INSERT INTO notification_events (notification_id, device_id, event_type)
        SELECT id, device_id, 'opened'
        FROM matched
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/notifications/{id:guid}/dismiss", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH updated AS (
            UPDATE notifications
            SET dismissed_at = NOW()
            WHERE id = @id AND device_id = @deviceId AND dismissed_at IS NULL
            RETURNING id, device_id
        )
        INSERT INTO notification_events (notification_id, device_id, event_type)
        SELECT id, device_id, 'dismissed'
        FROM updated
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/notifications/clear-read", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH updated AS (
            UPDATE notifications
            SET dismissed_at = NOW()
            WHERE device_id = @deviceId AND is_read = TRUE AND dismissed_at IS NULL
            RETURNING id, device_id
        )
        INSERT INTO notification_events (notification_id, device_id, event_type)
        SELECT id, device_id, 'dismissed'
        FROM updated
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/notifications/mute", async (
    MuteNotificationsRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var muteUntil = request.Duration switch
    {
        "1h" => DateTimeOffset.UtcNow.AddHours(1),
        "today" => DateTimeOffset.UtcNow.Date.AddDays(1).AddTicks(-1),
        "7d" => DateTimeOffset.UtcNow.AddDays(7),
        "indefinite" => DateTimeOffset.MaxValue,
        _ => (DateTimeOffset?)null,
    };

    if (muteUntil is null)
    {
        return Results.BadRequest(new { error = "INVALID_DURATION", message = "Gecerli degerler: 1h, today, 7d, indefinite" });
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE users
        SET notification_preferences = jsonb_set(
            COALESCE(notification_preferences, '{}'),
            '{mutedUntil}',
            to_jsonb(@muteUntil::text)
        ),
        updated_at = NOW()
        WHERE id = @userId AND deleted_at IS NULL
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("muteUntil", muteUntil.Value.ToString("O"));
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapDelete("/api/v1/notifications/mute", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE users
        SET notification_preferences = notification_preferences - 'mutedUntil',
            updated_at = NOW()
        WHERE id = @userId AND deleted_at IS NULL
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/admin/auth/login", async (
    AdminLoginRequest request,
    HttpContext httpContext,
    AdminAuthService adminAuth,
    BruteForceService bruteForce,
    Db db,
    RedisService redis,
    EmailService emailService,
    ILogger<Program> logger
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    // IP allowlist kontrolÃ¼
    if (!adminAuth.IsIpAllowed(httpContext))
    {
        return Unauthorized();
    }

    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var ipBlock = IpAddressPrivacy.ToNetworkBlock(httpContext.Connection.RemoteIpAddress) ?? "unknown";
    var bfIdentity = BruteForceService.IdentityFor(ip, "admin-login");

    if (await bruteForce.IsLockedOutAsync(bfIdentity))
    {
        return TooManyRequests("ACCOUNT_LOCKED", "Ã‡ok fazla baÅŸarÄ±sÄ±z deneme. 15 dakika bekleyin.");
    }

    if (!adminAuth.ValidateCredentials(request.Email, request.Password))
    {
        await bruteForce.RecordFailedAttemptAsync(bfIdentity);
        await using var connection = await db.OpenConnectionAsync();
        await LogAdminActionAsync(connection, null!, request.Email, "login_failed", "auth", null, $"IP block: {ipBlock}");
        return Unauthorized();
    }

    // SMTP yapılandırılmamışsa OTP adımını atla; IP allowlist ve strong password güvenliği sağlar.
    // SMTP yapılandırıldığında e-posta OTP otomatik devreye girer.
    if (!emailService.IsConfigured)
    {
        await bruteForce.ClearAsync(bfIdentity);
        return Results.Ok(new AdminLoginResponse(
            adminAuth.IssueToken(),
            DateTimeOffset.UtcNow.AddHours(4)
        ));
    }

    var otpKey = $"otp:admin:{request.Email.ToLowerInvariant()}";
    if (string.IsNullOrWhiteSpace(request.TotpCode))
    {
        var (otp, tooSoon, waitSecs, _) = await GetOrCreateCachedOtpAsync(
            redis.GetDb(), otpKey,
            validFor: TimeSpan.FromMinutes(10),
            resendAfter: TimeSpan.FromMinutes(3));

        if (tooSoon)
            return TooManyRequests("OTP_TOO_SOON", $"Yeni kod icin {waitSecs} saniye bekleyin.", waitSecs);

        try
        {
            await emailService.SendAdminLoginOtpAsync(request.Email, otp!);
            return Results.Ok(new { requiresEmailOtp = true });
        }
        catch (Exception ex)
        {
            // Mail gönderilemedi — şifre doğrulaması + IP allowlist yeterli güvenlik sağlar
            logger.LogError(ex, "Admin OTP e-postası gönderilemedi, fallback login: {Email}", request.Email);
            await bruteForce.ClearAsync(bfIdentity);
            return Results.Ok(new AdminLoginResponse(
                adminAuth.IssueToken(),
                DateTimeOffset.UtcNow.AddHours(4)
            ));
        }
    }

    if (!await ValidateCachedOtpAsync(redis.GetDb(), otpKey, request.TotpCode))
    {
        await bruteForce.RecordFailedAttemptAsync(bfIdentity);
        return Unauthorized();
    }

    await redis.GetDb().KeyDeleteAsync(otpKey);

    await bruteForce.ClearAsync(bfIdentity);

    return Results.Ok(new AdminLoginResponse(
        adminAuth.IssueToken(),
        DateTimeOffset.UtcNow.AddHours(4)
    ));
}).RequireRateLimiting("auth-strict");

app.MapGet("/api/v1/admin/moderation/queue", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string status = "under_review",
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM (
            SELECT id FROM posts WHERE status = @status
            UNION ALL
            SELECT id FROM comments WHERE status = @status
        ) queue
        """,
        connection
    );
    countCommand.Parameters.AddWithValue("status", status);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        """
        SELECT *
        FROM (
            SELECT p.id,
                   'post' AS target_type,
                   p.title,
                   p.content,
                   p.status,
                   p.moderation_reason,
                   p.created_at,
                   p.device_id,
                   p.perspective_toxicity,
                   p.image_url,
                   (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') AS report_count
            FROM posts p
            WHERE p.status = @status

            UNION ALL

            SELECT c.id,
                   'comment' AS target_type,
                   'Yorum' AS title,
                   c.content,
                   c.status,
                   c.moderation_reason,
                   c.created_at,
                   c.device_id,
                   NULL::double precision AS perspective_toxicity,
                   NULL::text AS image_url,
                   (SELECT COUNT(*) FROM reports r WHERE r.target_type = 'comment' AND r.target_id = c.id AND r.status = 'pending') AS report_count
            FROM comments c
            WHERE c.status = @status
        ) queue
        ORDER BY report_count DESC, created_at ASC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("status", status);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var items = new List<ModerationQueueItem>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new ModerationQueueItem(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            Convert.ToInt32(reader.GetInt64(10)),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9)
        ));
    }

    return Results.Ok(new PagedResponse<ModerationQueueItem>(
        items,
        new PagedPagination(page, limit, total, offset + items.Count < total)
    ));
});

app.MapPost("/api/v1/admin/moderation/{targetType}/{targetId:guid}/{action}", async (
    string targetType,
    Guid targetId,
    string action,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    if (targetType is not ("post" or "comment"))
    {
        return BadRequest("INVALID_TARGET_TYPE", "Hedef tipi post veya comment olmalÄ±.");
    }

    var nextStatus = action switch
    {
        "approve" => "active",
        "hide" => "auto_hidden",
        "delete" => "deleted",
        _ => null
    };
    if (nextStatus is null)
    {
        return BadRequest("INVALID_ADMIN_ACTION", "Aksiyon approve, hide veya delete olmalÄ±.");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var affected = await UpdateModerationTargetAsync(connection, transaction, targetType, targetId, nextStatus);
    if (affected == 0)
    {
        await transaction.RollbackAsync();
        return NotFound("TARGET_NOT_FOUND", "Moderasyon hedefi bulunamadÄ±.");
    }

    await MarkReportsForTargetAsync(
        connection,
        transaction,
        targetType,
        targetId,
        action == "approve" ? "dismissed" : "actioned"
    );
    await LogAdminActionAsync(connection, transaction, adminEmail, action, targetType, targetId, null);
    await transaction.CommitAsync();

    return Results.NoContent();
});

app.MapPost("/api/v1/admin/moderation/bulk", async (
    BulkModerationRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    var nextStatus = request.Action switch
    {
        "approve" => "active",
        "hide" => "auto_hidden",
        "delete" => "deleted",
        _ => null
    };
    if (nextStatus is null) return BadRequest("INVALID_ACTION", "Aksiyon geÃ§ersiz.");

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var successCount = 0;
    foreach (var item in request.Items)
    {
        var affected = await UpdateModerationTargetAsync(connection, transaction, item.Type, item.Id, nextStatus);
        if (affected > 0)
        {
            successCount++;
            await MarkReportsForTargetAsync(
                connection,
                transaction,
                item.Type,
                item.Id,
                request.Action == "approve" ? "dismissed" : "actioned"
            );
            await LogAdminActionAsync(connection, transaction, adminEmail, $"bulk_{request.Action}", item.Type, item.Id, null);
        }
    }

    await transaction.CommitAsync();
    return Results.Ok(new { successCount });
});

app.MapGet("/api/v1/admin/reports", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string status = "pending",
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        "SELECT COUNT(*) FROM reports WHERE status = @status",
        connection
    );
    countCommand.Parameters.AddWithValue("status", status);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        """
        SELECT r.id, r.target_type, r.target_id, r.reason, r.description, r.status, r.created_at,
               CASE
                   WHEN r.target_type = 'post' THEN (SELECT content FROM posts WHERE id = r.target_id)
                   WHEN r.target_type = 'comment' THEN (SELECT content FROM comments WHERE id = r.target_id)
               END as target_content,
               CASE
                   WHEN r.target_type = 'post' THEN (SELECT title FROM posts WHERE id = r.target_id)
               END as target_title
        FROM reports r
        WHERE r.status = @status
        ORDER BY r.created_at ASC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("status", status);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var reports = new List<AdminReportDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        reports.Add(new AdminReportDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            TargetContent: reader.IsDBNull(7) ? null : reader.GetString(7),
            TargetTitle: reader.IsDBNull(8) ? null : reader.GetString(8)
        ));
    }

    return Results.Ok(new PagedResponse<AdminReportDto>(
        reports,
        new PagedPagination(page, limit, total, offset + reports.Count < total)
    ));
});

app.MapPost("/api/v1/admin/reports/{id:guid}/action", async (
    Guid id,
    AdminReportActionRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    ReporterReputationService reputationService
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.Action is not ("actioned" or "dismissed"))
    {
        return BadRequest("INVALID_REPORT_ACTION", "Rapor aksiyonu actioned veya dismissed olmalÄ±.");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE reports
        SET status = @status
        WHERE id = @id AND status = 'pending'
        RETURNING target_type, target_id
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("status", request.Action);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return NotFound("REPORT_NOT_FOUND", "Bekleyen rapor bulunamadÄ±.");
    }

    var targetType = reader.GetString(0);
    var targetId = reader.GetGuid(1);
    await reader.CloseAsync();
    await reputationService.RecordOutcomeAsync(connection, transaction, id, request.Action == "actioned");
    await LogAdminActionAsync(connection, transaction, adminEmail, $"report_{request.Action}", targetType, targetId, request.Note);
    await transaction.CommitAsync();

    return Results.NoContent();
});

app.MapGet("/api/v1/admin/devices", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string search = "",
    bool? banned = null,
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;
    search = search.Trim();
    var bannedWhere = banned is null ? "" : "AND d.is_banned = @banned";
    var searchWhere = string.IsNullOrWhiteSpace(search)
        ? ""
        : "AND (d.id::text ILIKE @search OR d.fingerprint ILIKE @search OR d.platform ILIKE @search)";

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM devices d
        WHERE TRUE {bannedWhere} {searchWhere}
        """,
        connection
    );
    if (banned is not null)
    {
        countCommand.Parameters.AddWithValue("banned", banned.Value);
    }
    if (!string.IsNullOrWhiteSpace(search))
    {
        countCommand.Parameters.AddWithValue("search", $"%{search}%");
    }
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        $"""
        SELECT d.id,
               d.fingerprint,
               d.platform,
               d.is_banned,
               d.created_at,
               d.last_seen_at,
               (SELECT COUNT(*) FROM posts p WHERE p.device_id = d.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments c WHERE c.device_id = d.id AND c.status != 'deleted') AS comment_count,
               (SELECT COUNT(*) FROM reports r WHERE r.reporter_device_id = d.id) AS report_count,
               b.reason,
               b.expires_at
        FROM devices d
        LEFT JOIN LATERAL (
            SELECT reason, expires_at
            FROM bans
            WHERE device_id = d.id
            ORDER BY created_at DESC
            LIMIT 1
        ) b ON TRUE
        WHERE TRUE {bannedWhere} {searchWhere}
        ORDER BY d.last_seen_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    if (banned is not null)
    {
        command.Parameters.AddWithValue("banned", banned.Value);
    }
    if (!string.IsNullOrWhiteSpace(search))
    {
        command.Parameters.AddWithValue("search", $"%{search}%");
    }
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var devices = new List<AdminDeviceDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        devices.Add(ReadAdminDevice(reader));
    }

    return Results.Ok(new PagedResponse<AdminDeviceDto>(
        devices,
        new PagedPagination(page, limit, total, offset + devices.Count < total)
    ));
});

app.MapGet("/api/v1/admin/devices/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT d.id,
               d.fingerprint,
               d.platform,
               d.is_banned,
               d.created_at,
               d.last_seen_at,
               (SELECT COUNT(*) FROM posts p WHERE p.device_id = d.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments c WHERE c.device_id = d.id AND c.status != 'deleted') AS comment_count,
               (SELECT COUNT(*) FROM reports r WHERE r.reporter_device_id = d.id) AS report_count,
               b.reason,
               b.expires_at
        FROM devices d
        LEFT JOIN LATERAL (
            SELECT reason, expires_at
            FROM bans
            WHERE device_id = d.id
            ORDER BY created_at DESC
            LIMIT 1
        ) b ON TRUE
        WHERE d.id = @id
        """,
        connection
    );
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return NotFound("DEVICE_NOT_FOUND", "Cihaz bulunamadi.");
    }

    return Results.Ok(ReadAdminDevice(reader));
});

app.MapPost("/api/v1/admin/devices/{id:guid}/ban", async (
    Guid id,
    AdminBanDeviceRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.Type is not ("temporary" or "permanent"))
    {
        return BadRequest("INVALID_BAN_TYPE", "Ban tipi temporary veya permanent olmalÄ±.");
    }

    var expiresAt = request.Type == "temporary"
        ? DateTimeOffset.UtcNow.AddDays(request.DurationDays ?? 7)
        : (DateTimeOffset?)null;

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO bans (device_id, type, reason, expires_at)
        VALUES (@deviceId, @type, @reason, @expiresAt);

        UPDATE devices
        SET is_banned = TRUE
        WHERE id = @deviceId;
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("deviceId", id);
    command.Parameters.AddWithValue("type", request.Type);
    command.Parameters.AddWithValue("reason", request.Reason);
    command.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "ban_device", "device", id, request.Reason);
    await transaction.CommitAsync();

    return Results.NoContent();
});

app.MapPost("/api/v1/admin/devices/{id:guid}/unban", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE devices SET is_banned = FALSE WHERE id = @deviceId",
        connection,
        transaction
    );
    command.Parameters.AddWithValue("deviceId", id);
    var affected = await command.ExecuteNonQueryAsync();
    if (affected == 0)
    {
        await transaction.RollbackAsync();
        return NotFound("DEVICE_NOT_FOUND", "Cihaz bulunamadÄ±.");
    }

    await LogAdminActionAsync(connection, transaction, adminEmail, "unban_device", "device", id, null);
    await transaction.CommitAsync();

    return Results.NoContent();
});

// â"€â"€ AUTH â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapGet("/api/v1/admin/posts", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string status = "all",
    string search = "",
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    if (status is not ("all" or "active" or "under_review" or "auto_hidden" or "deleted"))
    {
        return BadRequest("INVALID_STATUS", "Gecersiz post durumu.");
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;
    search = search.Trim();
    var statusWhere = status == "all" ? "" : "AND status = @status";
    var searchWhere = string.IsNullOrWhiteSpace(search)
        ? ""
        : "AND (title ILIKE @search OR content ILIKE @search)";

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM posts
        WHERE TRUE {statusWhere} {searchWhere}
        """,
        connection
    );
    if (status != "all")
    {
        countCommand.Parameters.AddWithValue("status", status);
    }
    if (!string.IsNullOrWhiteSpace(search))
    {
        countCommand.Parameters.AddWithValue("search", $"%{search}%");
    }

    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        $"""
        SELECT p.id, p.title, p.content, p.status, p.category_id, p.vote_count_hakli,
               p.vote_count_haksiz, p.comment_count, p.image_url, p.created_at, p.device_id,
               p.user_id, u.username, p.is_anonymous
        FROM posts p
        LEFT JOIN users u ON u.id = p.user_id
        WHERE TRUE {statusWhere} {searchWhere}
        ORDER BY p.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    if (status != "all")
    {
        command.Parameters.AddWithValue("status", status);
    }
    if (!string.IsNullOrWhiteSpace(search))
    {
        command.Parameters.AddWithValue("search", $"%{search}%");
    }
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var posts = new List<AdminPostDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        posts.Add(new AdminPostDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            !reader.IsDBNull(13) && reader.GetBoolean(13)
        ));
    }

    return Results.Ok(new PagedResponse<AdminPostDto>(
        posts,
        new PagedPagination(page, limit, total, offset + posts.Count < total)
    ));
});

app.MapDelete("/api/v1/admin/posts/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET status = 'deleted',
            updated_at = NOW()
        WHERE id = @id AND status != 'deleted'
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);
    var affected = await command.ExecuteNonQueryAsync();
    if (affected == 0)
    {
        await transaction.RollbackAsync();
        return NotFound("POST_NOT_FOUND", "Post bulunamadi.");
    }

    await MarkReportsForTargetAsync(connection, transaction, "post", id, "actioned");
    await LogAdminActionAsync(connection, transaction, adminEmail, "delete_post", "post", id, null);
    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/admin/comments", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    Guid? postId = null,
    string status = "all",
    string search = "",
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    if (status is not ("all" or "active" or "under_review" or "auto_hidden" or "deleted"))
    {
        return BadRequest("INVALID_STATUS", "Gecersiz yorum durumu.");
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;
    search = search.Trim();
    var postWhere = postId is null ? "" : "AND post_id = @postId";
    var statusWhere = status == "all" ? "" : "AND status = @status";
    var searchWhere = string.IsNullOrWhiteSpace(search) ? "" : "AND content ILIKE @search";

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM comments
        WHERE TRUE {postWhere} {statusWhere} {searchWhere}
        """,
        connection
    );
    AddAdminCommentFilters(countCommand, postId, status, search);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        $"""
        SELECT id, post_id, content, status, upvote_count, downvote_count, created_at, device_id, user_id
        FROM comments
        WHERE TRUE {postWhere} {statusWhere} {searchWhere}
        ORDER BY created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    AddAdminCommentFilters(command, postId, status, search);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var comments = new List<AdminCommentDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        comments.Add(ReadAdminComment(reader));
    }

    return Results.Ok(new PagedResponse<AdminCommentDto>(
        comments,
        new PagedPagination(page, limit, total, offset + comments.Count < total)
    ));
});

app.MapDelete("/api/v1/admin/comments/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        WITH existing AS (
            SELECT post_id, status
            FROM comments
            WHERE id = @id AND status != 'deleted'
        ),
        updated AS (
            UPDATE comments
            SET status = 'deleted',
                updated_at = NOW()
            WHERE id = @id AND status != 'deleted'
        )
        SELECT post_id, status
        FROM existing
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return NotFound("COMMENT_NOT_FOUND", "Yorum bulunamadi.");
    }

    var postId = reader.GetGuid(0);
    var previousStatus = reader.GetString(1);
    await reader.CloseAsync();

    if (previousStatus == "active")
    {
        await using var updatePost = new NpgsqlCommand(
            """
            UPDATE posts
            SET comment_count = GREATEST(0, comment_count - 1),
                trend_score = (vote_count_hakli + vote_count_haksiz + (GREATEST(0, comment_count - 1) * 2))
                    / POWER(EXTRACT(EPOCH FROM (NOW() - created_at)) / 3600 + 2, 1.5),
                updated_at = NOW()
            WHERE id = @postId
            """,
            connection,
            transaction
        );
        updatePost.Parameters.AddWithValue("postId", postId);
        await updatePost.ExecuteNonQueryAsync();
    }

    await MarkReportsForTargetAsync(connection, transaction, "comment", id, "actioned");
    await LogAdminActionAsync(connection, transaction, adminEmail, "delete_comment", "comment", id, null);
    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/admin/users", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string search = "",
    bool? banned = null,
    int page = 1,
    int limit = 30,
    string? role = null,
    string sortBy = "assigned_at",
    string sortDir = "desc"
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    // ── Role listing mode ─────────────────────────────────────────────────
    // Returns users assigned to a specific admin role with RoleUser shape.
    if (role is not null)
    {
        if (!new[] { "superadmin", "moderator", "analyst" }.Contains(role))
            return Results.BadRequest(new { error = "INVALID_ROLE" });

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);
        var roleOffset = (page - 1) * limit;

        var orderCol = sortBy switch
        {
            "username" => "u.username",
            "action_count" => "action_count_30d",
            _ => "ar.assigned_at",
        };
        var direction = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        await using var roleConn = await db.OpenConnectionAsync();

        await using var roleCountCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM admin_roles ar JOIN users u ON u.id = ar.user_id WHERE ar.role = @role AND u.deleted_at IS NULL",
            roleConn
        );
        roleCountCmd.Parameters.AddWithValue("role", role);
        var roleTotal = Convert.ToInt32(await roleCountCmd.ExecuteScalarAsync());

        await using var roleCmd = new NpgsqlCommand(
            $"""
            SELECT
                u.id,
                u.username,
                u.email,
                ar.role,
                ar.assigned_at,
                u.updated_at AS last_active_at,
                COALESCE((
                    SELECT COUNT(*)
                    FROM admin_actions aa
                    WHERE aa.admin_email = u.email
                      AND aa.created_at >= NOW() - INTERVAL '30 days'
                ), 0) AS action_count_30d
            FROM admin_roles ar
            JOIN users u ON u.id = ar.user_id
            WHERE ar.role = @role
              AND u.deleted_at IS NULL
            ORDER BY {orderCol} {direction}
            LIMIT @limit OFFSET @offset
            """,
            roleConn
        );
        roleCmd.Parameters.AddWithValue("role", role);
        roleCmd.Parameters.AddWithValue("limit", limit);
        roleCmd.Parameters.AddWithValue("offset", roleOffset);

        var roleUsers = new List<object>();
        await using var roleReader = await roleCmd.ExecuteReaderAsync();
        while (await roleReader.ReadAsync())
        {
            roleUsers.Add(new
            {
                id = roleReader.GetGuid(0).ToString(),
                username = roleReader.GetString(1),
                email = roleReader.GetString(2),
                role = roleReader.GetString(3),
                assignedAt = roleReader.GetFieldValue<DateTimeOffset>(4),
                lastActiveAt = roleReader.IsDBNull(5) ? (DateTimeOffset?)null : roleReader.GetFieldValue<DateTimeOffset>(5),
                actionCount30d = Convert.ToInt32(roleReader.GetInt64(6)),
            });
        }

        return Results.Ok(new { users = roleUsers, total = roleTotal, page, pageSize = limit });
    }

    // ── Standard user listing / search mode ──────────────────────────────
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;
    search = search.Trim();
    var bannedWhere = banned is null ? "" : "AND u.is_banned = @banned";
    var searchWhere = string.IsNullOrWhiteSpace(search)
        ? ""
        : "AND (u.id::text ILIKE @search OR u.username ILIKE @search OR u.email ILIKE @search)";

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM users u
        WHERE TRUE {bannedWhere} {searchWhere}
        """,
        connection
    );
    AddAdminUserFilters(countCommand, banned, search);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        $"""
        SELECT u.id,
               u.device_id,
               u.username,
               u.email,
               u.karma,
               u.auth_provider,
               u.email_verified,
               u.is_banned,
               u.ban_expires_at,
               u.ban_reason,
               u.created_at,
               u.deleted_at,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments c WHERE c.user_id = u.id AND c.status != 'deleted') AS comment_count
        FROM users u
        WHERE TRUE {bannedWhere} {searchWhere}
        ORDER BY u.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    AddAdminUserFilters(command, banned, search);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var users = new List<AdminUserDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(ReadAdminUser(reader));
    }

    return Results.Ok(new PagedResponse<AdminUserDto>(
        users,
        new PagedPagination(page, limit, total, offset + users.Count < total)
    ));
});

app.MapGet("/api/v1/admin/users/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    await using var connection = await db.OpenConnectionAsync();

    await using var userCmd = new NpgsqlCommand(
        """
        SELECT u.id, u.device_id, u.username, COALESCE(u.email, ''), u.karma,
               u.auth_provider, u.email_verified, u.is_banned, u.ban_expires_at,
               u.ban_reason, u.created_at, u.deleted_at,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status != 'deleted'),
               (SELECT COUNT(*) FROM comments c WHERE c.user_id = u.id AND c.status != 'deleted')
        FROM users u
        WHERE u.id = @id
        """,
        connection
    );
    userCmd.Parameters.AddWithValue("id", id);
    await using var userReader = await userCmd.ExecuteReaderAsync();
    if (!await userReader.ReadAsync())
    {
        return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");
    }
    var user = ReadAdminUser(userReader);
    await userReader.CloseAsync();

    await using var postsCmd = new NpgsqlCommand(
        """
        SELECT id, title, status, created_at
        FROM posts
        WHERE user_id = @id
        ORDER BY created_at DESC
        LIMIT 10
        """,
        connection
    );
    postsCmd.Parameters.AddWithValue("id", id);
    var recentPosts = new List<AdminUserPostDto>();
    await using var postsReader = await postsCmd.ExecuteReaderAsync();
    while (await postsReader.ReadAsync())
    {
        recentPosts.Add(new AdminUserPostDto(
            postsReader.GetGuid(0),
            postsReader.GetString(1),
            postsReader.GetString(2),
            postsReader.GetFieldValue<DateTimeOffset>(3)
        ));
    }
    await postsReader.CloseAsync();

    await using var strikesCmd = new NpgsqlCommand(
        """
        SELECT id, reason, severity, created_at
        FROM user_strikes
        WHERE user_id = @id
        ORDER BY created_at DESC
        """,
        connection
    );
    strikesCmd.Parameters.AddWithValue("id", id);
    var strikes = new List<AdminUserStrikeDto>();
    await using var strikesReader = await strikesCmd.ExecuteReaderAsync();
    while (await strikesReader.ReadAsync())
    {
        strikes.Add(new AdminUserStrikeDto(
            strikesReader.GetGuid(0),
            strikesReader.GetString(1),
            strikesReader.GetString(2),
            strikesReader.GetFieldValue<DateTimeOffset>(3)
        ));
    }

    return Results.Ok(new AdminUserDetailDto(user, recentPosts, strikes));
});

app.MapPost("/api/v1/admin/users/{id:guid}/ban", async (
    Guid id,
    AdminBanUserRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var expiresAt = request.DurationDays is null
        ? (DateTimeOffset?)null
        : DateTimeOffset.UtcNow.AddDays(request.DurationDays.Value);

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = new NpgsqlCommand(
        """
        UPDATE users
        SET is_banned = TRUE,
            ban_reason = @reason,
            ban_expires_at = @expiresAt,
            updated_at = NOW()
        WHERE id = @id AND deleted_at IS NULL
        RETURNING device_id
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("reason", request.Reason);
    command.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);
    var deviceResult = await command.ExecuteScalarAsync();
    if (deviceResult is not Guid deviceId)
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    }

    await using var revokeTokens = new NpgsqlCommand(
        "UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @userId AND revoked_at IS NULL",
        connection,
        transaction
    );
    revokeTokens.Parameters.AddWithValue("userId", id);
    await revokeTokens.ExecuteNonQueryAsync();

    await using var notification = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body)
        VALUES (@deviceId, 'moderation_result', 'Hesap kisitlandi', @body)
        """,
        connection,
        transaction
    );
    notification.Parameters.AddWithValue("deviceId", deviceId);
    notification.Parameters.AddWithValue("body", request.Reason);
    await notification.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "ban_user", "user", id, request.Reason);
    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/admin/users/{id:guid}/unban", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE users
        SET is_banned = FALSE,
            ban_reason = NULL,
            ban_expires_at = NULL,
            updated_at = NOW()
        WHERE id = @id AND deleted_at IS NULL
        RETURNING id
        """,
        connection,
        transaction
    );
    cmd.Parameters.AddWithValue("id", id);
    var result = await cmd.ExecuteScalarAsync();
    if (result is null)
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    }

    await LogAdminActionAsync(connection, transaction, adminEmail, "unban_user", "user", id, null);
    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/admin/users/{id:guid}/warn", async (
    Guid id,
    AdminWarnUserRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var findUser = new NpgsqlCommand(
        "SELECT device_id FROM users WHERE id = @id AND deleted_at IS NULL",
        connection,
        transaction
    );
    findUser.Parameters.AddWithValue("id", id);
    var deviceResult = await findUser.ExecuteScalarAsync();
    if (deviceResult is not Guid deviceId)
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    }

    await using var notification = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body)
        VALUES (@deviceId, 'moderation_result', 'Moderator uyarisi', @body)
        """,
        connection,
        transaction
    );
    notification.Parameters.AddWithValue("deviceId", deviceId);
    notification.Parameters.AddWithValue("body", request.Message);
    await notification.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "warn_user", "user", id, request.Message);
    await transaction.CommitAsync();
    return Results.NoContent();
});

// ── ADMIN ENFORCEMENT LADDER — STRIKE ────────────────────────────────────

app.MapGet("/api/v1/admin/users/{id:guid}/strikes", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT s.id, s.admin_email, s.reason, s.severity, s.note, s.created_at
        FROM user_strikes s
        WHERE s.user_id = @userId
        ORDER BY s.created_at DESC
        LIMIT 50
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", id);

    var strikes = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        strikes.Add(new
        {
            id = reader.GetGuid(0),
            adminEmail = reader.GetString(1),
            reason = reader.GetString(2),
            severity = reader.GetString(3),
            note = reader.IsDBNull(4) ? null : reader.GetString(4),
            createdAt = reader.GetFieldValue<DateTimeOffset>(5),
        });
    }

    return Results.Ok(new { userId = id, total = strikes.Count, strikes });
});

app.MapPost("/api/v1/admin/users/{id:guid}/strike", async (
    Guid id,
    AdminStrikeUserRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    // Auth before validation: don't leak field names or endpoint shape to unauthorized callers.
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var validSeverities = new HashSet<string> { "light", "medium", "heavy" };
    if (!validSeverities.Contains(request.Severity))
        return Results.BadRequest(new { error = "INVALID_SEVERITY", message = "Severity must be light, medium, or heavy." });

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var findUser = new NpgsqlCommand(
        "SELECT device_id FROM users WHERE id = @id AND deleted_at IS NULL",
        connection, transaction
    );
    findUser.Parameters.AddWithValue("id", id);
    var deviceResult = await findUser.ExecuteScalarAsync();
    if (deviceResult is not Guid deviceId)
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    }

    await using var insertStrike = new NpgsqlCommand(
        """
        INSERT INTO user_strikes (user_id, admin_email, reason, severity, note)
        VALUES (@userId, @adminEmail, @reason, @severity, @note)
        """,
        connection, transaction
    );
    insertStrike.Parameters.AddWithValue("userId", id);
    insertStrike.Parameters.AddWithValue("adminEmail", adminEmail);
    insertStrike.Parameters.AddWithValue("reason", request.Reason);
    insertStrike.Parameters.AddWithValue("severity", request.Severity);
    insertStrike.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
    await insertStrike.ExecuteNonQueryAsync();

    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM user_strikes WHERE user_id = @userId",
        connection, transaction
    );
    countCmd.Parameters.AddWithValue("userId", id);
    var totalStrikes = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    string? autoAction = null;
    if (request.Severity == "heavy")
    {
        autoAction = "permanent_ban";
        await using var permBan = new NpgsqlCommand(
            """
            UPDATE users
            SET is_banned = TRUE, ban_reason = @reason, ban_expires_at = NULL, updated_at = NOW()
            WHERE id = @id AND deleted_at IS NULL;
            INSERT INTO bans (device_id, type, reason)
            VALUES (@deviceId, 'permanent', @reason);
            """,
            connection, transaction
        );
        permBan.Parameters.AddWithValue("id", id);
        permBan.Parameters.AddWithValue("deviceId", deviceId);
        permBan.Parameters.AddWithValue("reason", $"Agir ihlal: {request.Reason}");
        await permBan.ExecuteNonQueryAsync();
    }
    else if (totalStrikes >= 5)
    {
        autoAction = "permanent_ban";
        await using var permBan = new NpgsqlCommand(
            """
            UPDATE users
            SET is_banned = TRUE, ban_reason = @reason, ban_expires_at = NULL, updated_at = NOW()
            WHERE id = @id AND deleted_at IS NULL;
            INSERT INTO bans (device_id, type, reason)
            VALUES (@deviceId, 'permanent', @reason);
            """,
            connection, transaction
        );
        permBan.Parameters.AddWithValue("id", id);
        permBan.Parameters.AddWithValue("deviceId", deviceId);
        permBan.Parameters.AddWithValue("reason", $"Tekrarlayan ihlal ({totalStrikes}. uyari): {request.Reason}");
        await permBan.ExecuteNonQueryAsync();
    }
    else if (totalStrikes >= 3)
    {
        autoAction = "temp_ban_30d";
        var banUntil = DateTimeOffset.UtcNow.AddDays(30);
        await using var tempBan = new NpgsqlCommand(
            """
            UPDATE users
            SET is_banned = TRUE, ban_reason = @reason, ban_expires_at = @expiresAt, updated_at = NOW()
            WHERE id = @id AND deleted_at IS NULL;
            INSERT INTO bans (device_id, type, reason, expires_at)
            VALUES (@deviceId, 'temporary', @reason, @expiresAt);
            """,
            connection, transaction
        );
        tempBan.Parameters.AddWithValue("id", id);
        tempBan.Parameters.AddWithValue("deviceId", deviceId);
        tempBan.Parameters.AddWithValue("reason", $"Tekrarlayan ihlal ({totalStrikes}. uyari): {request.Reason}");
        tempBan.Parameters.AddWithValue("expiresAt", banUntil);
        await tempBan.ExecuteNonQueryAsync();
    }

    var notifBody = autoAction switch
    {
        "permanent_ban" => "Platformun kullanim kurallarina tekrarlayan ihlaller nedeniyle hesabiniz kalici olarak askiya alinmistir.",
        "temp_ban_30d" => "Kurallara aykiri davranislariniz nedeniyle hesabiniz 30 gun askiya alinmistir.",
        _ => $"Hesabiniz kural ihlali nedeniyle uyarilmistir: {request.Reason}",
    };

    await using var notification = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body)
        VALUES (@deviceId, 'moderation_result', 'Hesap uyarisi', @body)
        """,
        connection, transaction
    );
    notification.Parameters.AddWithValue("deviceId", deviceId);
    notification.Parameters.AddWithValue("body", notifBody);
    await notification.ExecuteNonQueryAsync();

    var actionNote = $"severity={request.Severity}, total_strikes={totalStrikes}" +
                     (autoAction != null ? $", auto_action={autoAction}" : "") +
                     $", reason={request.Reason}";
    await LogAdminActionAsync(connection, transaction, adminEmail, "strike_user", "user", id, actionNote);
    await transaction.CommitAsync();

    return Results.Ok(new
    {
        totalStrikes,
        autoAction,
        userId = id,
    });
});

// ── ADMIN KVKK DELETE ────────────────────────────────────────────────────

app.MapPost("/api/v1/admin/users/{id:guid}/delete", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    ComplianceLogService complianceLog
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var exists = await new NpgsqlCommand(
        "SELECT EXISTS(SELECT 1 FROM users WHERE id = @id AND deleted_at IS NULL)",
        connection,
        transaction
    ) { Parameters = { new("id", id) } }.ExecuteScalarAsync();

    if (exists is not true)
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı veya zaten silinmiş.");
    }

    // KVKK hard delete: kişisel verileri anonimleştir
    await using var anonCmd = new NpgsqlCommand(
        """
        UPDATE users SET
            username        = 'deleted_' || SUBSTR(id::text, 1, 8),
            email           = NULL,
            password_hash   = NULL,
            totp_secret     = NULL,
            bio             = NULL,
            deleted_at      = NOW()
        WHERE id = @id
        """,
        connection,
        transaction
    );
    anonCmd.Parameters.AddWithValue("id", id);
    await anonCmd.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "kvkk_delete", "user", id, null);
    await transaction.CommitAsync();

    await complianceLog.LogAsync(
        "kvkk_user_delete",
        GetClientIpBlock(httpRequest),
        null,
        id,
        null,
        null,
        new { adminEmail, deletedUserId = id }
    );

    return Results.NoContent();
});

// ── ADMIN USERS BULK ACTION ──────────────────────────────────────────────

app.MapPost("/api/v1/admin/users/bulk", async (
    BulkUserActionRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (request.Action is not ("ban" or "warn" or "unban"))
        return BadRequest("INVALID_ACTION", "Aksiyon geçersiz.");

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var successCount = 0;
    foreach (var userId in request.UserIds)
    {
        if (request.Action == "ban")
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET status = 'banned' WHERE id = @id AND status != 'banned'",
                connection, transaction);
            cmd.Parameters.AddWithValue("id", userId);
            var affected = await cmd.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                await new NpgsqlCommand(
                    "INSERT INTO ban_history (user_id, type, reason, admin_email) VALUES (@uid, 'ban', @reason, @admin)",
                    connection, transaction)
                {
                    Parameters = {
                        new("uid", userId),
                        new("reason", (object?)request.Reason ?? DBNull.Value),
                        new("admin", adminEmail)
                    }
                }.ExecuteNonQueryAsync();
                await LogAdminActionAsync(connection, transaction, adminEmail, "user_banned", "user", userId, request.Reason);
                successCount++;
            }
        }
        else if (request.Action == "warn")
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET status = 'warned' WHERE id = @id AND status = 'active'",
                connection, transaction);
            cmd.Parameters.AddWithValue("id", userId);
            var affected = await cmd.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                await new NpgsqlCommand(
                    "INSERT INTO ban_history (user_id, type, reason, admin_email) VALUES (@uid, 'warn', @reason, @admin)",
                    connection, transaction)
                {
                    Parameters = {
                        new("uid", userId),
                        new("reason", (object?)request.Reason ?? DBNull.Value),
                        new("admin", adminEmail)
                    }
                }.ExecuteNonQueryAsync();
                await LogAdminActionAsync(connection, transaction, adminEmail, "user_warned", "user", userId, request.Reason);
                successCount++;
            }
        }
        else if (request.Action == "unban")
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET status = 'active' WHERE id = @id AND status = 'banned'",
                connection, transaction);
            cmd.Parameters.AddWithValue("id", userId);
            var affected = await cmd.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                await LogAdminActionAsync(connection, transaction, adminEmail, "user_unbanned", "user", userId, request.Reason);
                successCount++;
            }
        }
    }

    await transaction.CommitAsync();
    return Results.Ok(new { successCount });
});

// ── ADMIN ROLE MANAGEMENT ────────────────────────────────────────────────

app.MapPost("/api/v1/admin/users/{id:guid}/role", async (
    Guid id,
    AdminAssignRoleRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (!new[] { "superadmin", "moderator", "analyst" }.Contains(request.Role))
        return BadRequest("INVALID_ROLE", "Geçersiz rol.");

    await using var connection = await db.OpenConnectionAsync();

    await using var checkCmd = new NpgsqlCommand(
        "SELECT id FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    checkCmd.Parameters.AddWithValue("id", id);
    if (await checkCmd.ExecuteScalarAsync() is null) return Results.NotFound();

    await using var upsertCmd = new NpgsqlCommand(
        """
        INSERT INTO admin_roles (user_id, role, assigned_by)
        VALUES (@userId, @role, @assignedBy)
        ON CONFLICT (user_id) DO UPDATE SET role = @role, assigned_at = NOW(), assigned_by = @assignedBy
        """,
        connection
    );
    upsertCmd.Parameters.AddWithValue("userId", id);
    upsertCmd.Parameters.AddWithValue("role", request.Role);
    upsertCmd.Parameters.AddWithValue("assignedBy", adminEmail);
    await upsertCmd.ExecuteNonQueryAsync();

    await using var auditCmd = new NpgsqlCommand(
        "INSERT INTO admin_actions (admin_email, action, target_type, target_id, note) VALUES (@email, 'role_assigned', 'user', @targetId, @note)",
        connection
    );
    auditCmd.Parameters.AddWithValue("email", adminEmail);
    auditCmd.Parameters.AddWithValue("targetId", id);
    auditCmd.Parameters.AddWithValue("note", (object)request.Role);
    await auditCmd.ExecuteNonQueryAsync();

    return Results.NoContent();
});

app.MapDelete("/api/v1/admin/users/{id:guid}/role", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "DELETE FROM admin_roles WHERE user_id = @userId",
        connection
    );
    cmd.Parameters.AddWithValue("userId", id);
    await cmd.ExecuteNonQueryAsync();

    await using var auditCmd = new NpgsqlCommand(
        "INSERT INTO admin_actions (admin_email, action, target_type, target_id, note) VALUES (@email, 'role_revoked', 'user', @targetId, NULL)",
        connection
    );
    auditCmd.Parameters.AddWithValue("email", adminEmail);
    auditCmd.Parameters.AddWithValue("targetId", id);
    await auditCmd.ExecuteNonQueryAsync();

    return Results.NoContent();
});

// ── ADMIN MODERATION NOTIFY ──────────────────────────────────────────────

app.MapPost("/api/v1/admin/moderation/{targetType}/{targetId:guid}/notify", async (
    string targetType,
    Guid targetId,
    AdminNotifyRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (targetType is not ("post" or "comment"))
        return BadRequest("INVALID_TARGET_TYPE", "Hedef tipi post veya comment olmalı.");

    await using var connection = await db.OpenConnectionAsync();

    // post veya comment'ten device_id ve user_id'yi bul
    var table = targetType == "post" ? "posts" : "comments";
    await using var findCmd = new NpgsqlCommand(
        $"SELECT device_id, user_id FROM {table} WHERE id = @id",
        connection
    );
    findCmd.Parameters.AddWithValue("id", targetId);
    await using var findReader = await findCmd.ExecuteReaderAsync();
    if (!await findReader.ReadAsync())
        return NotFound("TARGET_NOT_FOUND", "İçerik bulunamadı.");

    var deviceId = findReader.GetGuid(0);
    var userId = findReader.IsDBNull(1) ? (Guid?)null : findReader.GetGuid(1);
    await findReader.CloseAsync();

    var ruleViolated = request.RuleViolated.Trim();
    var message = request.Message.Trim();
    var notificationBody = $"Kural ihlali: {ruleViolated}\n\n{message}";
    var notificationPayload = JsonSerializer.Serialize(new
    {
        target_type = targetType,
        target_id = targetId,
        rule_violated = ruleViolated,
        message = message,
        appeal_path = "/settings/moderation-history"
    });

    await using var transaction = await connection.BeginTransactionAsync();

    await using var notifyCmd = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body, post_id, payload)
        VALUES (@deviceId, 'moderation_result', @title, @body, @postId, @payload::jsonb)
        """,
        connection,
        transaction
    );
    notifyCmd.Parameters.AddWithValue("deviceId", deviceId);
    notifyCmd.Parameters.AddWithValue("title", "Moderasyon Kararı");
    notifyCmd.Parameters.AddWithValue("body", notificationBody);
    notifyCmd.Parameters.AddWithValue("postId", targetType == "post" ? (object)targetId : DBNull.Value);
    notifyCmd.Parameters.AddWithValue("payload", notificationPayload);
    await notifyCmd.ExecuteNonQueryAsync();

    await using var updateReasonCmd = new NpgsqlCommand(
        $"UPDATE {table} SET moderation_reason = @reason WHERE id = @targetId",
        connection,
        transaction
    );
    updateReasonCmd.Parameters.AddWithValue("reason", ruleViolated);
    updateReasonCmd.Parameters.AddWithValue("targetId", targetId);
    await updateReasonCmd.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "notify_user", targetType, targetId, $"{ruleViolated}: {message}");
    await transaction.CommitAsync();

    return Results.NoContent();
});

// ── ADMIN USERS DATA EXPORT ──────────────────────────────────────────────

app.MapGet("/api/v1/admin/users/{id:guid}/data-export", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();

    await using var userCmd = new NpgsqlCommand(
        """
        SELECT id, username, email, created_at, bio, deleted_at
        FROM users WHERE id = @id
        """,
        connection
    );
    userCmd.Parameters.AddWithValue("id", id);
    await using var ur = await userCmd.ExecuteReaderAsync();
    if (!await ur.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    var exportUser = new
    {
        Id = ur.GetGuid(0),
        Username = ur.GetString(1),
        Email = ur.IsDBNull(2) ? null : ur.GetString(2),
        CreatedAt = ur.GetFieldValue<DateTimeOffset>(3),
        Bio = ur.IsDBNull(4) ? null : ur.GetString(4),
        DeletedAt = ur.IsDBNull(5) ? (DateTimeOffset?)null : ur.GetFieldValue<DateTimeOffset>(5)
    };
    await ur.CloseAsync();

    await using var postsCmd = new NpgsqlCommand(
        "SELECT id, title, content, status, created_at FROM posts WHERE user_id = @id ORDER BY created_at DESC",
        connection
    );
    postsCmd.Parameters.AddWithValue("id", id);
    var posts = new List<object>();
    await using var pr = await postsCmd.ExecuteReaderAsync();
    while (await pr.ReadAsync())
        posts.Add(new { Id = pr.GetGuid(0), Title = pr.GetString(1), Content = pr.GetString(2), Status = pr.GetString(3), CreatedAt = pr.GetFieldValue<DateTimeOffset>(4) });
    await pr.CloseAsync();

    await using var commentsCmd = new NpgsqlCommand(
        "SELECT id, content, status, created_at FROM comments WHERE user_id = @id ORDER BY created_at DESC",
        connection
    );
    commentsCmd.Parameters.AddWithValue("id", id);
    var comments = new List<object>();
    await using var cr = await commentsCmd.ExecuteReaderAsync();
    while (await cr.ReadAsync())
        comments.Add(new { Id = cr.GetGuid(0), Content = cr.GetString(1), Status = cr.GetString(2), CreatedAt = cr.GetFieldValue<DateTimeOffset>(3) });
    await cr.CloseAsync();

    await using var votesCmd = new NpgsqlCommand(
        "SELECT post_id, vote_type, created_at FROM votes WHERE user_id = @id ORDER BY created_at DESC",
        connection
    );
    votesCmd.Parameters.AddWithValue("id", id);
    var votes = new List<object>();
    await using var vr = await votesCmd.ExecuteReaderAsync();
    while (await vr.ReadAsync())
        votes.Add(new { PostId = vr.GetGuid(0), VoteType = vr.GetString(1), CreatedAt = vr.GetFieldValue<DateTimeOffset>(2) });
    await vr.CloseAsync();

    return Results.Ok(new { User = exportUser, Posts = posts, Comments = comments, Votes = votes, ExportedAt = DateTimeOffset.UtcNow });
});

// ── ADMIN MODERATION APPEALS ─────────────────────────────────────────────

app.MapGet("/api/v1/admin/appeals", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string status = "pending",
    int page = 1,
    int pageSize = 20
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 50);
    var offset = (page - 1) * pageSize;

    if (status is not ("pending" or "approved" or "rejected")) status = "pending";

    await using var connection = await db.OpenConnectionAsync();

    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM moderation_appeals WHERE status = @status",
        connection);
    countCmd.Parameters.AddWithValue("status", status);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT
            a.id, a.user_id, u.username,
            a.target_type, a.target_id,
            a.message, a.status, a.created_at,
            a.reviewed_at, a.reviewed_by, a.review_note,
            CASE
                WHEN a.target_type = 'post' THEN (SELECT title FROM posts WHERE id = a.target_id)
                WHEN a.target_type = 'comment' THEN (SELECT LEFT(content, 100) FROM comments WHERE id = a.target_id)
            END AS target_preview
        FROM moderation_appeals a
        JOIN users u ON u.id = a.user_id
        WHERE a.status = @status
        ORDER BY a.created_at ASC
        LIMIT @limit OFFSET @offset
        """,
        connection);
    cmd.Parameters.AddWithValue("status", status);
    cmd.Parameters.AddWithValue("limit", pageSize);
    cmd.Parameters.AddWithValue("offset", offset);

    var appeals = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        appeals.Add(new
        {
            id = reader.GetGuid(0),
            userId = reader.GetGuid(1),
            username = reader.GetString(2),
            targetType = reader.GetString(3),
            targetId = reader.GetGuid(4),
            message = reader.GetString(5),
            status = reader.GetString(6),
            createdAt = reader.GetFieldValue<DateTimeOffset>(7),
            reviewedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8),
            reviewedBy = reader.IsDBNull(9) ? null : reader.GetString(9),
            reviewNote = reader.IsDBNull(10) ? null : reader.GetString(10),
            targetPreview = reader.IsDBNull(11) ? null : reader.GetString(11),
        });
    }

    return Results.Ok(new { appeals, total, page, pageSize });
});

app.MapPost("/api/v1/admin/appeals/{id:guid}/decide", async (
    Guid id,
    AdminAppealDecisionRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (request.Decision is not ("approved" or "rejected"))
        return BadRequest("INVALID_DECISION", "Karar approved veya rejected olmalı.");

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var updateCmd = new NpgsqlCommand(
        """
        UPDATE moderation_appeals
        SET status = @decision, reviewed_at = NOW(), reviewed_by = @admin, review_note = @note
        WHERE id = @id AND status = 'pending'
        RETURNING target_type, target_id
        """,
        connection, transaction);
    updateCmd.Parameters.AddWithValue("id", id);
    updateCmd.Parameters.AddWithValue("decision", request.Decision);
    updateCmd.Parameters.AddWithValue("admin", adminEmail);
    updateCmd.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);

    await using var reader = await updateCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return NotFound("APPEAL_NOT_FOUND", "Başvuru bulunamadı veya zaten incelendi.");
    }

    var targetType = reader.GetString(0);
    var targetId = reader.GetGuid(1);
    await reader.CloseAsync();

    if (request.Decision == "approved")
    {
        var table = targetType == "post" ? "posts" : "comments";
        await new NpgsqlCommand(
            $"UPDATE {table} SET status = 'active' WHERE id = @tid",
            connection, transaction)
        { Parameters = { new("tid", targetId) } }.ExecuteNonQueryAsync();
    }

    await LogAdminActionAsync(connection, transaction, adminEmail,
        request.Decision == "approved" ? "appeal_approved" : "appeal_rejected",
        targetType, targetId, request.Note);

    await transaction.CommitAsync();
    return Results.NoContent();
});

// ── ADMIN AUTOMOD RULES ───────────────────────────────────────────────────

app.MapGet("/api/v1/admin/automod/rules", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT id, name, rule_type, pattern, config, action, is_active, created_by, created_at FROM automod_rules ORDER BY created_at DESC",
        connection);

    var rules = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rules.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            ruleType = reader.GetString(2),
            pattern = reader.IsDBNull(3) ? null : reader.GetString(3),
            config = reader.IsDBNull(4) ? null : reader.GetString(4),
            action = reader.GetString(5),
            isActive = reader.GetBoolean(6),
            createdBy = reader.GetString(7),
            createdAt = reader.GetFieldValue<DateTimeOffset>(8),
        });
    }

    return Results.Ok(new { rules });
});

app.MapPost("/api/v1/admin/automod/rules", async (
    CreateAutomodRuleRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (request.RuleType is not ("keyword" or "regex" or "behavior"))
        return BadRequest("INVALID_RULE_TYPE", "Kural tipi keyword, regex veya behavior olmalı.");

    if (request.Action is not ("hide" or "queue" or "suspend" or "flag"))
        return BadRequest("INVALID_ACTION", "Aksiyon geçersiz.");

    if (request.RuleType is "keyword" or "regex" && string.IsNullOrEmpty(request.Pattern))
        return BadRequest("PATTERN_REQUIRED", "Keyword/regex kuralları için pattern zorunlu.");

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO automod_rules (name, rule_type, pattern, config, action, is_active, created_by)
        VALUES (@name, @ruleType, @pattern, @config::jsonb, @action, TRUE, @admin)
        RETURNING id
        """,
        connection);
    cmd.Parameters.AddWithValue("name", request.Name);
    cmd.Parameters.AddWithValue("ruleType", request.RuleType);
    cmd.Parameters.AddWithValue("pattern", (object?)request.Pattern ?? DBNull.Value);
    cmd.Parameters.AddWithValue("config", (object?)request.Config ?? DBNull.Value);
    cmd.Parameters.AddWithValue("action", request.Action);
    cmd.Parameters.AddWithValue("admin", adminEmail);

    var id = (Guid)(await cmd.ExecuteScalarAsync())!;
    return Results.Created($"/api/v1/admin/automod/rules/{id}", new { id });
});

app.MapPatch("/api/v1/admin/automod/rules/{id:guid}", async (
    Guid id,
    ToggleAutomodRuleRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "UPDATE automod_rules SET is_active = @active, updated_at = NOW() WHERE id = @id",
        connection);
    cmd.Parameters.AddWithValue("active", request.IsActive);
    cmd.Parameters.AddWithValue("id", id);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows == 0 ? NotFound("RULE_NOT_FOUND", "Kural bulunamadı.") : Results.NoContent();
});

app.MapDelete("/api/v1/admin/automod/rules/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("DELETE FROM automod_rules WHERE id = @id", connection);
    cmd.Parameters.AddWithValue("id", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});

// ── ADMIN POST FEATURE ──────────────────────────────────────────────────

app.MapPost("/api/v1/admin/posts/{id:guid}/feature", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        UPDATE posts
        SET is_featured = NOT is_featured,
            featured_at = CASE WHEN NOT is_featured THEN NOW() ELSE NULL END
        WHERE id = @id AND status = 'active'
        RETURNING is_featured
        """,
        connection,
        transaction
    );
    cmd.Parameters.AddWithValue("id", id);

    var result = await cmd.ExecuteScalarAsync();
    if (result is null)
    {
        await transaction.RollbackAsync();
        return NotFound("POST_NOT_FOUND", "Post bulunamadı veya aktif değil.");
    }

    var isFeatured = (bool)result;
    await LogAdminActionAsync(connection, transaction, adminEmail, isFeatured ? "feature_post" : "unfeature_post", "post", id, null);
    await transaction.CommitAsync();

    return Results.Ok(new { IsFeatured = isFeatured });
});

app.MapPost("/api/v1/admin/posts/{id:guid}/hide", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    // Toggle: active/under_review → auto_hidden, auto_hidden → active
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE posts
        SET status = CASE
                WHEN status = 'auto_hidden' THEN 'active'
                ELSE 'auto_hidden'
            END,
            updated_at = NOW()
        WHERE id = @id AND status != 'deleted'
        RETURNING status
        """,
        connection,
        transaction
    );
    cmd.Parameters.AddWithValue("id", id);

    var result = await cmd.ExecuteScalarAsync();
    if (result is null)
    {
        await transaction.RollbackAsync();
        return NotFound("POST_NOT_FOUND", "Post bulunamadı veya silinmiş.");
    }

    var newStatus = (string)result;
    var action = newStatus == "auto_hidden" ? "hide_post" : "unhide_post";
    await LogAdminActionAsync(connection, transaction, adminEmail, action, "post", id, null);
    await transaction.CommitAsync();

    return Results.Ok(new { Status = newStatus });
});

// ── ADMIN DEVICES SUSPICIOUS ─────────────────────────────────────────────

app.MapGet("/api/v1/admin/devices/suspicious", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();

    var total = Convert.ToInt32(await new NpgsqlCommand(
        "SELECT COUNT(*) FROM device_trust_scores WHERE is_suspicious = TRUE",
        connection
    ).ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT d.id, d.fingerprint, d.platform, d.is_banned, d.created_at, d.last_seen_at,
               (SELECT COUNT(*) FROM posts p WHERE p.device_id = d.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments c WHERE c.device_id = d.id AND c.status != 'deleted') AS comment_count,
               (SELECT COUNT(*) FROM reports r WHERE r.reporter_device_id = d.id) AS report_count,
               dts.suspicious_reason,
               dts.failed_integrity_count,
               dts.trust_score
        FROM device_trust_scores dts
        JOIN devices d ON d.id = dts.device_id
        WHERE dts.is_suspicious = TRUE
        ORDER BY dts.trust_score ASC, dts.failed_integrity_count DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var devices = new List<AdminSuspiciousDeviceDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        devices.Add(new AdminSuspiciousDeviceDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            Convert.ToInt32(reader.GetInt64(6)),
            Convert.ToInt32(reader.GetInt64(7)),
            Convert.ToInt32(reader.GetInt64(8)),
            reader.IsDBNull(9) ? "unknown" : reader.GetString(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) ? 1.0 : reader.GetDouble(11)
        ));
    }

    return Results.Ok(new PagedResponse<AdminSuspiciousDeviceDto>(
        devices,
        new PagedPagination(page, limit, total, offset + devices.Count < total)
    ));
});

// ── ADMIN TRUST SCORE HISTORY ────────────────────────────────────────────

app.MapGet("/api/v1/admin/devices/{deviceId}/trust-history", async (
    string deviceId,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT score, reason, recorded_at
        FROM device_trust_score_history
        WHERE device_id = @deviceId
          AND recorded_at >= NOW() - INTERVAL '90 days'
        ORDER BY recorded_at DESC
        LIMIT 500
        """,
        connection
    );
    cmd.Parameters.AddWithValue("deviceId", deviceId);

    var history = new List<DeviceTrustHistoryDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        history.Add(new DeviceTrustHistoryDto(
            reader.GetDouble(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2)
        ));
    }

    return Results.Ok(history);
});

// ── ADMIN ENFORCEMENT ────────────────────────────────────────────────────

app.MapPost("/api/v1/admin/enforcement", async (
    ApplyEnforcementRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();
    if (ValidateRequest(request) is { } ve) return ve;

    if (request.TargetType is not ("device" or "user"))
        return BadRequest("INVALID_TARGET_TYPE", "target_type 'device' veya 'user' olmalı.");

    if (request.Action is not ("warning" or "strike" or "temp_ban" or "perm_ban"))
        return BadRequest("INVALID_ACTION", "Geçerli aksiyonlar: warning, strike, temp_ban, perm_ban.");

    if (request.Action is "temp_ban" && request.ExpiresAt is null)
        return BadRequest("MISSING_EXPIRES_AT", "temp_ban için expires_at zorunludur.");

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    // Insert enforcement record
    await using var insertCmd = new NpgsqlCommand(
        """
        INSERT INTO enforcement_actions (target_type, target_id, action, reason, expires_at, created_by_admin_id)
        VALUES (@targetType, @targetId, @action, @reason, @expiresAt, @adminId)
        RETURNING id
        """,
        connection,
        transaction
    );
    insertCmd.Parameters.AddWithValue("targetType", request.TargetType);
    insertCmd.Parameters.AddWithValue("targetId", request.TargetId);
    insertCmd.Parameters.AddWithValue("action", request.Action);
    insertCmd.Parameters.AddWithValue("reason", (object?)request.Reason ?? DBNull.Value);
    insertCmd.Parameters.AddWithValue("expiresAt", (object?)request.ExpiresAt ?? DBNull.Value);
    insertCmd.Parameters.AddWithValue("adminId", adminEmail);
    var actionId = (long)(await insertCmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("INSERT enforcement_actions failed"));

    // Yaptırım merdiveni: uyarı sayısını say; 3. uyarıda otomatik strike
    if (request.Action is "warning" && request.TargetType is "device")
    {
        await using var countCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM enforcement_actions
            WHERE target_type = 'device' AND target_id = @targetId AND action = 'warning'
            """,
            connection,
            transaction
        );
        countCmd.Parameters.AddWithValue("targetId", request.TargetId);
        var warningCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        if (warningCount >= 3)
        {
            // Auto-strike on 3rd warning
            await using var strikeCmd = new NpgsqlCommand(
                """
                INSERT INTO enforcement_actions (target_type, target_id, action, reason, created_by_admin_id)
                VALUES ('device', @targetId, 'strike', 'auto_strike_after_3_warnings', @adminId)
                """,
                connection,
                transaction
            );
            strikeCmd.Parameters.AddWithValue("targetId", request.TargetId);
            strikeCmd.Parameters.AddWithValue("adminId", adminEmail);
            await strikeCmd.ExecuteNonQueryAsync();

            // Increment strike_count on device
            if (Guid.TryParse(request.TargetId, out var deviceGuid))
            {
                await using var incrCmd = new NpgsqlCommand(
                    "UPDATE devices SET strike_count = strike_count + 1 WHERE id = @id",
                    connection,
                    transaction
                );
                incrCmd.Parameters.AddWithValue("id", deviceGuid);
                await incrCmd.ExecuteNonQueryAsync();
            }
        }
    }

    if (request.Action is "strike" && request.TargetType is "device"
        && Guid.TryParse(request.TargetId, out var strikeDeviceGuid))
    {
        await using var incrCmd = new NpgsqlCommand(
            "UPDATE devices SET strike_count = strike_count + 1 WHERE id = @id",
            connection,
            transaction
        );
        incrCmd.Parameters.AddWithValue("id", strikeDeviceGuid);
        await incrCmd.ExecuteNonQueryAsync();
    }

    await LogAdminActionAsync(connection, transaction, adminEmail, $"enforcement_{request.Action}", request.TargetType, null, request.Reason);
    await transaction.CommitAsync();

    return Results.Ok(new { id = actionId });
});

app.MapGet("/api/v1/admin/enforcement/{targetId}", async (
    string targetId,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string? targetType = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    var typeFilter = string.IsNullOrWhiteSpace(targetType) ? "" : "AND target_type = @targetType";

    await using var cmd = new NpgsqlCommand(
        $"""
        SELECT id, target_type, target_id, action, reason, expires_at, created_by_admin_id, created_at
        FROM enforcement_actions
        WHERE target_id = @targetId {typeFilter}
        ORDER BY created_at DESC
        LIMIT 200
        """,
        connection
    );
    cmd.Parameters.AddWithValue("targetId", targetId);
    if (!string.IsNullOrWhiteSpace(targetType))
        cmd.Parameters.AddWithValue("targetType", targetType);

    var actions = new List<EnforcementActionDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        actions.Add(new EnforcementActionDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7)
        ));
    }

    return Results.Ok(actions);
});

// ── ADMIN ALERTS ─────────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/alerts", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    bool resolved = false,
    int page = 1,
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();

    await using var countAlertCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM admin_alerts WHERE is_resolved = @resolved",
        connection
    );
    countAlertCmd.Parameters.AddWithValue("resolved", resolved);
    var total = Convert.ToInt32(await countAlertCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT id, type, payload, is_resolved, created_at
        FROM admin_alerts
        WHERE is_resolved = @resolved
        ORDER BY created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("resolved", resolved);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var alerts = new List<AdminAlertDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        alerts.Add(new AdminAlertDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetFieldValue<System.Text.Json.JsonElement>(2),
            reader.GetBoolean(3),
            reader.GetFieldValue<DateTimeOffset>(4)
        ));
    }

    return Results.Ok(new PagedResponse<AdminAlertDto>(
        alerts,
        new PagedPagination(page, limit, total, offset + alerts.Count < total)
    ));
});

app.MapPost("/api/v1/admin/alerts/{id:long}/resolve", async (
    long id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE admin_alerts
        SET is_resolved = TRUE, resolved_by = @adminEmail, resolved_at = NOW()
        WHERE id = @id AND is_resolved = FALSE
        """,
        connection
    );
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("adminEmail", adminEmail);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows == 0 ? NotFound("ALERT_NOT_FOUND", "Alert bulunamadı veya zaten çözümlendi.") : Results.NoContent();
});

// ── ADMIN BRIGADE ALERTS ──────────────────────────────────────────────────

app.MapGet("/api/v1/admin/brigade/alerts", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string status = "open",
    int limit = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    limit = Math.Clamp(limit, 1, 100);

    await using var connection = await db.OpenConnectionAsync();

    var resolvedFilter = status switch
    {
        "open" => "AND a.is_resolved = FALSE",
        "reviewed" => "AND a.is_resolved = TRUE",
        _ => ""
    };

    await using var countCmd = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM admin_alerts a
        WHERE a.type = 'brigade_suspected'
        {resolvedFilter}
        """,
        connection);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        $"""
        SELECT
            a.id,
            (a.payload->>'post_id')::uuid                      AS post_id,
            COALESCE(p.title, '[Silinmiş Post]')               AS post_title,
            (a.payload->>'device_count')::int                  AS device_count,
            COALESCE((a.payload->>'ip_concentration')::float, 0) AS ip_concentration,
            a.is_resolved,
            a.created_at
        FROM admin_alerts a
        LEFT JOIN posts p ON p.id = (a.payload->>'post_id')::uuid
        WHERE a.type = 'brigade_suspected'
        {resolvedFilter}
        ORDER BY a.created_at DESC
        LIMIT @limit
        """,
        connection);
    cmd.Parameters.AddWithValue("limit", limit);

    var alerts = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var deviceCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
        var ipConc = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4);
        var isResolved = reader.GetBoolean(5);

        var severity = ipConc >= 0.8 ? "high" : ipConc >= 0.65 ? "medium" : "low";
        var alertStatus = isResolved ? "reviewed" : "open";

        alerts.Add(new
        {
            id = reader.GetInt64(0).ToString(),
            postId = reader.IsDBNull(1) ? null : reader.GetGuid(1).ToString(),
            postTitle = reader.GetString(2),
            detectedAt = reader.GetFieldValue<DateTimeOffset>(6).ToString("O"),
            suspiciousUserCount = deviceCount,
            suspiciousVoteCount = deviceCount,
            severity,
            status = alertStatus,
        });
    }

    return Results.Ok(new { alerts, total });
});

app.MapPost("/api/v1/admin/brigade/alerts/{id:long}/resolve", async (
    long id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE admin_alerts
        SET is_resolved = TRUE, resolved_by = @adminEmail, resolved_at = NOW()
        WHERE id = @id AND type = 'brigade_suspected' AND is_resolved = FALSE
        """,
        connection);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("adminEmail", adminEmail);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows == 0
        ? NotFound("ALERT_NOT_FOUND", "Alert bulunamadı veya zaten çözümlendi.")
        : Results.NoContent();
});

// ── ADMIN DEVICES BAN SUBNET ─────────────────────────────────────────────

app.MapPost("/api/v1/admin/devices/ban-subnet", async (
    AdminBanSubnetRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null) return Unauthorized();

    if (ValidateRequest(request) is { } ve) return ve;

    if (string.IsNullOrWhiteSpace(request.Subnet) || string.IsNullOrWhiteSpace(request.Reason))
        return BadRequest("INVALID_REQUEST", "Subnet ve reason zorunludur.");

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO banned_subnets (subnet, reason, admin_email)
        VALUES (@subnet, @reason, @adminEmail)
        ON CONFLICT (subnet) DO UPDATE SET reason = @reason, admin_email = @adminEmail
        """,
        connection,
        transaction
    );
    cmd.Parameters.AddWithValue("subnet", request.Subnet.Trim());
    cmd.Parameters.AddWithValue("reason", request.Reason);
    cmd.Parameters.AddWithValue("adminEmail", adminEmail);
    await cmd.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "ban_subnet", "subnet", null, $"{request.Subnet}: {request.Reason}");
    await transaction.CommitAsync();

    return Results.NoContent();
});

// ── ADMIN CATEGORY THROTTLE ──────────────────────────────────────────────

app.MapGet("/api/v1/admin/categories/{id:int}/throttle", async (
    int id,
    HttpRequest httpRequest,
    AdminAuthService adminAuth,
    CategoryThrottleService categoryThrottle
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var status = await categoryThrottle.GetStatusAsync(id);
    return Results.Ok(new
    {
        categoryId = id,
        isThrottled = status.IsThrottled,
        reason = status.Reason,
        remainingSeconds = status.Remaining?.TotalSeconds
    });
});

app.MapPost("/api/v1/admin/categories/{id:int}/throttle", async (
    int id,
    AdminThrottleCategoryRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    CategoryThrottleService categoryThrottle
) =>
{
    var email = adminAuth.TryGetAdminEmail(httpRequest);
    if (email is null)
        return Unauthorized();

    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var duration = TimeSpan.FromHours(Math.Clamp(request.DurationHours, 1, 168));
    await categoryThrottle.SetThrottledAsync(id, duration, request.Reason);

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await LogAdminActionAsync(connection, transaction, email, "throttle_category", "category",
        null, $"categoryId={id}, duration={duration.TotalHours}h, reason={request.Reason}");
    await transaction.CommitAsync();

    return Results.NoContent();
});

app.MapDelete("/api/v1/admin/categories/{id:int}/throttle", async (
    int id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    CategoryThrottleService categoryThrottle
) =>
{
    var email = adminAuth.TryGetAdminEmail(httpRequest);
    if (email is null)
        return Unauthorized();

    await categoryThrottle.ClearThrottleAsync(id);

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await LogAdminActionAsync(connection, transaction, email, "unthrottle_category", "category",
        null, $"categoryId={id}");
    await transaction.CommitAsync();

    return Results.NoContent();
});

// ── ADMIN ACTIONS LOG ─────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/actions", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string action = "",
    string adminEmail = "",
    int page = 1,
    int limit = 50
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 100);
    var offset = (page - 1) * limit;
    action = action.Trim();
    adminEmail = adminEmail.Trim();

    var actionWhere = string.IsNullOrEmpty(action) ? "" : "AND aa.action = @action";
    var emailWhere = string.IsNullOrEmpty(adminEmail) ? "" : "AND aa.admin_email ILIKE @adminEmail";

    await using var connection = await db.OpenConnectionAsync();

    await using var countCmd = new NpgsqlCommand(
        $"""
        SELECT COUNT(*)
        FROM admin_actions aa
        WHERE TRUE {actionWhere} {emailWhere}
        """,
        connection
    );
    if (!string.IsNullOrEmpty(action)) countCmd.Parameters.AddWithValue("action", action);
    if (!string.IsNullOrEmpty(adminEmail)) countCmd.Parameters.AddWithValue("adminEmail", $"%{adminEmail}%");
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        $"""
        SELECT aa.id,
               aa.admin_email,
               aa.action,
               aa.target_type,
               aa.target_id,
               aa.note,
               aa.created_at
        FROM admin_actions aa
        WHERE TRUE {actionWhere} {emailWhere}
        ORDER BY aa.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    if (!string.IsNullOrEmpty(action)) command.Parameters.AddWithValue("action", action);
    if (!string.IsNullOrEmpty(adminEmail)) command.Parameters.AddWithValue("adminEmail", $"%{adminEmail}%");
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var items = new List<AdminActionDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new AdminActionDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6)
        ));
    }

    return Results.Ok(new PagedResponse<AdminActionDto>(
        items,
        new PagedPagination(page, limit, total, offset + items.Count < total)
    ));
});

app.MapPost("/api/v1/auth/register", async (
    RegisterRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    EmailService emailService,
    ComplianceLogService complianceLog,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    if (!request.AcceptedTerms || !request.AcceptedCommunityGuidelines)
    {
        return BadRequest(
            "POLICY_ACCEPTANCE_REQUIRED",
            "Kullanim kosullari ve topluluk kurallari kabul edilmeden hesap olusturulamaz.");
    }

    if (!request.AgeConfirmed)
    {
        return BadRequest(
            "AGE_CONFIRMATION_REQUIRED",
            "18 yaş ve üzeri olduğunu onaylamadan hesap oluşturamazsın.");
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var dob = DateOnly.FromDateTime(request.DateOfBirth);
    var age = today.Year - dob.Year;
    if (dob > today.AddYears(-age)) age--;
    if (age < 18)
    {
        return BadRequest(
            "UNDER_AGE",
            "Karar'a katılmak için 18 yaşında veya daha büyük olmalısın.");
    }

    await using var connection = await db.OpenConnectionAsync();

    // KullanÄ±cÄ± adÄ± veya e-posta daha Ã¶nce alÄ±nmÄ±ÅŸ mÄ±?
    await using var checkCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM users WHERE username = @username OR email = @email",
        connection
    );
    checkCmd.Parameters.AddWithValue("username", request.Username);
    checkCmd.Parameters.AddWithValue("email", request.Email.ToLowerInvariant());
    var existing = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
    if (existing > 0)
    {
        return Conflict("USER_EXISTS", "Bu kullanÄ±cÄ± adÄ± veya e-posta zaten kullanÄ±lÄ±yor.");
    }

    // Bekleyen OTP varsa temizle
    await using var deleteOtp = new NpgsqlCommand(
        "DELETE FROM email_otps WHERE email = @email",
        connection
    );
    deleteOtp.Parameters.AddWithValue("email", request.Email.ToLowerInvariant());
    await deleteOtp.ExecuteNonQueryAsync();

    var otp = PasswordService.GenerateOtp();
    var otpHash = PasswordService.HashOtp(otp);
    var passwordHash = PasswordService.Hash(request.Password);

    await using var transaction = await connection.BeginTransactionAsync();

    // Kullanıcıyı kaydet (doğrulanmamış)
    await using var insertUser = new NpgsqlCommand(
        """
        INSERT INTO users (device_id, username, email, email_verified, password_hash, auth_provider, date_of_birth, gender)
        VALUES (@deviceId, @username, @email, FALSE, @passwordHash, 'password', @dob, @gender)
        """,
        connection,
        transaction
    );
    insertUser.Parameters.AddWithValue("deviceId", deviceId.Value);
    insertUser.Parameters.AddWithValue("username", request.Username);
    insertUser.Parameters.AddWithValue("email", request.Email.ToLowerInvariant());
    insertUser.Parameters.AddWithValue("passwordHash", passwordHash);
    insertUser.Parameters.AddWithValue("dob", request.DateOfBirth);
    insertUser.Parameters.AddWithValue("gender", request.Gender);
    await insertUser.ExecuteNonQueryAsync();

    // OTP kaydet
    await using var insertOtp = new NpgsqlCommand(
        """
        INSERT INTO email_otps (email, otp_hash)
        VALUES (@email, @otpHash)
        """,
        connection,
        transaction
    );
    insertOtp.Parameters.AddWithValue("email", request.Email.ToLowerInvariant());
    insertOtp.Parameters.AddWithValue("otpHash", otpHash);
    await insertOtp.ExecuteNonQueryAsync();

    await transaction.CommitAsync();

    // Redis companion key — resend-otp için aynı kod politikası
    var verifyOtpKey = $"otp:verify:{request.Email.ToLowerInvariant()}";
    var nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await redis.GetDb().StringSetAsync(verifyOtpKey, $"{nowTs}|{otpHash}|{otp}", TimeSpan.FromMinutes(10));

    // OTP e-postasÄ±nÄ± arka planda gÃ¶nder
    _ = emailService.SendOtpAsync(request.Email, otp);

    var regIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("register", regIp, deviceId, null, null, "auth");

    return Results.Created(
        "/api/v1/auth/register",
        new RegisterResponse("DoÄŸrulama kodu e-postanÄ±za gÃ¶nderildi.", request.Email.ToLowerInvariant())
    );
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/verify-email", async (
    VerifyEmailRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var email = request.Email.ToLowerInvariant();
    var otpHash = PasswordService.HashOtp(request.Otp);

    await using var connection = await db.OpenConnectionAsync();

    // OTP kontrol et
    await using var otpCmd = new NpgsqlCommand(
        """
        SELECT id, otp_hash, attempts, expires_at
        FROM email_otps
        WHERE email = @email
        ORDER BY created_at DESC
        LIMIT 1
        """,
        connection
    );
    otpCmd.Parameters.AddWithValue("email", email);

    await using var otpReader = await otpCmd.ExecuteReaderAsync();
    if (!await otpReader.ReadAsync())
    {
        return BadRequest("OTP_NOT_FOUND", "Aktif doÄŸrulama kodu bulunamadÄ±.");
    }

    var otpId = otpReader.GetGuid(0);
    var storedHash = otpReader.GetString(1);
    var attempts = otpReader.GetInt32(2);
    var expiresAt = otpReader.GetFieldValue<DateTimeOffset>(3);
    await otpReader.CloseAsync();

    if (expiresAt < DateTimeOffset.UtcNow)
    {
        return BadRequest("OTP_EXPIRED", "DoÄŸrulama kodu sÃ¼resi dolmuÅŸ. Yeniden gÃ¶nder.");
    }

    if (attempts >= 3)
    {
        return BadRequest("OTP_MAX_ATTEMPTS", "Ã‡ok fazla hatalÄ± deneme. Yeni kod isteyin.");
    }

    if (!string.Equals(storedHash, otpHash, StringComparison.OrdinalIgnoreCase))
    {
        await using var incCmd = new NpgsqlCommand(
            "UPDATE email_otps SET attempts = attempts + 1 WHERE id = @id",
            connection
        );
        incCmd.Parameters.AddWithValue("id", otpId);
        await incCmd.ExecuteNonQueryAsync();
        return BadRequest("OTP_INVALID", $"Kod hatalÄ±. {2 - attempts} deneme hakkÄ±n kaldÄ±.");
    }

    // OTP doÄŸrulama baÅŸarÄ±lÄ±
    await using var transaction = await connection.BeginTransactionAsync();

    await using var verifyCmd = new NpgsqlCommand(
        """
        UPDATE users
        SET email_verified = TRUE, updated_at = NOW()
        WHERE email = @email AND email_verified = FALSE
        RETURNING id, username, karma, auth_provider
        """,
        connection,
        transaction
    );
    verifyCmd.Parameters.AddWithValue("email", email);
    await using var userReader = await verifyCmd.ExecuteReaderAsync();
    if (!await userReader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return BadRequest("USER_NOT_FOUND", "KullanÄ±cÄ± bulunamadÄ± veya zaten doÄŸrulanmÄ±ÅŸ.");
    }

    var userId = userReader.GetGuid(0);
    var username = userReader.GetString(1);
    var karma = userReader.GetInt32(2);
    var authProvider = userReader.GetString(3);
    await userReader.CloseAsync();

    // OTP sil
    await using var deleteOtp = new NpgsqlCommand(
        "DELETE FROM email_otps WHERE email = @email",
        connection,
        transaction
    );
    deleteOtp.Parameters.AddWithValue("email", email);
    await deleteOtp.ExecuteNonQueryAsync();

    var accessToken = jwtService.GenerateAccessToken(userId, username);
    var refreshToken = JwtService.GenerateRefreshToken();
    var refreshHash = JwtService.HashRefreshToken(refreshToken);

    await using var insertRefresh = new NpgsqlCommand(
        "INSERT INTO refresh_tokens (user_id, token_hash) VALUES (@userId, @hash)",
        connection,
        transaction
    );
    insertRefresh.Parameters.AddWithValue("userId", userId);
    insertRefresh.Parameters.AddWithValue("hash", refreshHash);
    await insertRefresh.ExecuteNonQueryAsync();

    await transaction.CommitAsync();

    return Results.Ok(new AuthTokensResponse(
        userId,
        username,
        accessToken,
        refreshToken,
        DateTimeOffset.UtcNow.AddMinutes(15),
        User: new UserProfile(userId, username, email, karma, authProvider, true)
    ));
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/resend-otp", async (
    ResendOtpRequest request,
    Db db,
    EmailService emailService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var email = request.Email.ToLowerInvariant();
    await using var connection = await db.OpenConnectionAsync();

    // Güvenlik: kullanıcı var/yok bilgisi verme — her durumda success dön
    await using var checkCmd = new NpgsqlCommand(
        "SELECT 1 FROM users WHERE email = @email AND email_verified = FALSE",
        connection
    );
    checkCmd.Parameters.AddWithValue("email", email);
    if (await checkCmd.ExecuteScalarAsync() is null)
        return Results.Ok(new MessageResponse("Kod yeniden gönderildi."));

    // Redis companion key: 3 dk cooldown, aynı pencerede aynı kod
    var otpCacheKey = $"otp:verify:{email}";
    var (otp, tooSoon, waitSecs, isNew) = await GetOrCreateCachedOtpAsync(
        redis.GetDb(), otpCacheKey,
        validFor: TimeSpan.FromMinutes(10),
        resendAfter: TimeSpan.FromMinutes(3));

    if (tooSoon)
        return TooManyRequests("OTP_TOO_SOON", $"Yeni kod için {waitSecs} saniye bekleyin.", waitSecs);

    // Yalnızca yeni OTP üretildiyse DB'yi güncelle; aynı kod resend ediliyorsa expires_at korunur
    if (isNew)
    {
        await using var deleteCmd = new NpgsqlCommand("DELETE FROM email_otps WHERE email = @email", connection);
        deleteCmd.Parameters.AddWithValue("email", email);
        await deleteCmd.ExecuteNonQueryAsync();

        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)", connection);
        insertCmd.Parameters.AddWithValue("email", email);
        insertCmd.Parameters.AddWithValue("hash", PasswordService.HashOtp(otp!));
        await insertCmd.ExecuteNonQueryAsync();
    }

    _ = emailService.SendOtpAsync(email, otp!);
    return Results.Ok(new MessageResponse("Kod gönderildi."));
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/google", async (
    GoogleSignInRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    FirebaseAuthService firebaseAuth
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var firebaseToken = await firebaseAuth.VerifyIdTokenAsync(request.IdToken);
    if (firebaseToken is null)
    {
        return BadRequest("INVALID_FIREBASE_TOKEN", "Google kimlik doÄŸrulama baÅŸarÄ±sÄ±z.");
    }

    var googleId = firebaseToken.Uid;
    var googleEmail = firebaseToken.Claims.TryGetValue("email", out var emailClaim)
        ? emailClaim.ToString()!.ToLowerInvariant()
        : null;

    if (string.IsNullOrEmpty(googleEmail))
    {
        return BadRequest("EMAIL_MISSING", "Google hesabÄ±nda e-posta bulunamadÄ±.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    // Mevcut kullanÄ±cÄ± var mÄ±? (google_id veya email ile bul)
    await using var findCmd = new NpgsqlCommand(
        """
        SELECT id, username, email, karma, auth_provider
        FROM users
        WHERE google_id = @googleId OR (email = @email AND auth_provider = 'google')
        LIMIT 1
        """,
        connection,
        transaction
    );
    findCmd.Parameters.AddWithValue("googleId", googleId);
    findCmd.Parameters.AddWithValue("email", googleEmail);

    await using var findReader = await findCmd.ExecuteReaderAsync();
    Guid userId;
    string username;
    string userEmail;
    int karma;
    string authProvider;
    bool isNewUser;

    if (await findReader.ReadAsync())
    {
        // Mevcut kullanÄ±cÄ± â€" gÃ¼ncelle
        userId = findReader.GetGuid(0);
        username = findReader.GetString(1);
        userEmail = findReader.GetString(2);
        karma = findReader.GetInt32(3);
        authProvider = findReader.GetString(4);
        isNewUser = false;
        await findReader.CloseAsync();

        await using var updateCmd = new NpgsqlCommand(
            "UPDATE users SET google_id = @googleId, updated_at = NOW() WHERE id = @id",
            connection,
            transaction
        );
        updateCmd.Parameters.AddWithValue("googleId", googleId);
        updateCmd.Parameters.AddWithValue("id", userId);
        await updateCmd.ExecuteNonQueryAsync();
    }
    else
    {
        await findReader.CloseAsync();
        isNewUser = true;

        // E-posta Google'dan alÄ±ndÄ±ÄŸÄ±ndan doÄŸrulanmÄ±ÅŸ kabul edilir
        var baseUsername = googleEmail.Split('@')[0]
            .Replace('.', '_')
            .Replace('-', '_');
        baseUsername = System.Text.RegularExpressions.Regex.Replace(baseUsername, @"[^a-zA-Z0-9_]", "");
        if (baseUsername.Length < 3) baseUsername = "user_" + baseUsername;
        if (baseUsername.Length > 16) baseUsername = baseUsername[..16];

        // Benzersiz username oluÅŸtur
        username = baseUsername;
        var suffix = 0;
        while (true)
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM users WHERE username = @username",
                connection,
                transaction
            );
            checkCmd.Parameters.AddWithValue("username", username);
            if (await checkCmd.ExecuteScalarAsync() is null) break;
            suffix++;
            username = $"{baseUsername}_{suffix}";
        }

        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO users (device_id, username, email, email_verified, auth_provider, google_id)
            VALUES (@deviceId, @username, @email, TRUE, 'google', @googleId)
            RETURNING id, karma
            """,
            connection,
            transaction
        );
        insertCmd.Parameters.AddWithValue("deviceId", deviceId.Value);
        insertCmd.Parameters.AddWithValue("username", username);
        insertCmd.Parameters.AddWithValue("email", googleEmail);
        insertCmd.Parameters.AddWithValue("googleId", googleId);
        await using var insertReader = await insertCmd.ExecuteReaderAsync();
        await insertReader.ReadAsync();
        userId = insertReader.GetGuid(0);
        karma = insertReader.GetInt32(1);
        await insertReader.CloseAsync();

        userEmail = googleEmail;
        authProvider = "google";
    }

    var accessToken = jwtService.GenerateAccessToken(userId, username);
    var refreshToken = JwtService.GenerateRefreshToken();
    var refreshHash = JwtService.HashRefreshToken(refreshToken);

    await using var insertRefresh = new NpgsqlCommand(
        "INSERT INTO refresh_tokens (user_id, token_hash) VALUES (@userId, @hash)",
        connection,
        transaction
    );
    insertRefresh.Parameters.AddWithValue("userId", userId);
    insertRefresh.Parameters.AddWithValue("hash", refreshHash);
    await insertRefresh.ExecuteNonQueryAsync();

    await transaction.CommitAsync();

    return isNewUser
        ? Results.Created("/api/v1/auth/google", new
        {
            userId,
            username,
            accessToken,
            refreshToken,
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            isNewUser = true,
            user = new UserProfile(userId, username, userEmail, 0, authProvider, true)
        })
        : Results.Ok(new AuthTokensResponse(
            userId,
            username,
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddMinutes(15),
            User: new UserProfile(userId, username, userEmail, karma, authProvider, true)
        ));
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/google/link", async (
    GoogleSignInRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    FirebaseAuthService firebaseAuth
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var firebaseToken = await firebaseAuth.VerifyIdTokenAsync(request.IdToken);
    if (firebaseToken is null)
    {
        return BadRequest("INVALID_FIREBASE_TOKEN", "Google kimlik doÄŸrulama baÅŸarÄ±sÄ±z.");
    }

    var googleId = firebaseToken.Uid;
    await using var connection = await db.OpenConnectionAsync();

    // Bu googleId baÅŸka birine baÄŸlÄ± mÄ±?
    await using var checkCmd = new NpgsqlCommand(
        "SELECT id FROM users WHERE google_id = @googleId AND id != @userId",
        connection
    );
    checkCmd.Parameters.AddWithValue("googleId", googleId);
    checkCmd.Parameters.AddWithValue("userId", userId);
    if (await checkCmd.ExecuteScalarAsync() is not null)
    {
        return Conflict("GOOGLE_ALREADY_LINKED", "Bu Google hesabÄ± baÅŸka bir kullanÄ±cÄ±ya baÄŸlÄ±.");
    }

    await using var updateCmd = new NpgsqlCommand(
        "UPDATE users SET google_id = @googleId, updated_at = NOW() WHERE id = @userId",
        connection
    );
    updateCmd.Parameters.AddWithValue("googleId", googleId);
    updateCmd.Parameters.AddWithValue("userId", userId);
    await updateCmd.ExecuteNonQueryAsync();

    return Results.NoContent();
}).RequireRateLimiting("auth-strict");

app.MapDelete("/api/v1/auth/google", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();

    // Google baÄŸlantÄ±sÄ±nÄ± kesmek iÃ§in ÅŸifre olmasÄ± ÅŸart (hesaba eriÅŸim kalmasÄ± iÃ§in)
    await using var findCmd = new NpgsqlCommand(
        "SELECT password_hash FROM users WHERE id = @userId",
        connection
    );
    findCmd.Parameters.AddWithValue("userId", userId);
    var passwordHash = await findCmd.ExecuteScalarAsync() as string;

    if (string.IsNullOrEmpty(passwordHash))
    {
        return BadRequest("PASSWORD_REQUIRED", "Google baÄŸlantÄ±sÄ±nÄ± kesmek iÃ§in Ã¶nce bir ÅŸifre belirlemelisiniz.");
    }

    await using var updateCmd = new NpgsqlCommand(
        "UPDATE users SET google_id = NULL, updated_at = NOW() WHERE id = @userId",
        connection
    );
    updateCmd.Parameters.AddWithValue("userId", userId);
    await updateCmd.ExecuteNonQueryAsync();

    return Results.NoContent();
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/login", async (
    LoginRequest request,
    HttpContext httpContext,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    BruteForceService bruteForce,
    ComplianceLogService complianceLog
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var bfIdentity = BruteForceService.IdentityFor(ip, "login", deviceId);

    if (await bruteForce.IsLockedOutAsync(bfIdentity))
    {
        return TooManyRequests("ACCOUNT_LOCKED", "Çok fazla başarısız deneme. 15 dakika bekleyin.");
    }

    // Progressive delay: 3+ başarısız denemede sunucu taraflı gecikme (brute-force yavaşlatma)
    var failedAttempts = await bruteForce.GetFailedAttemptCountAsync(bfIdentity);
    var delayMs = BruteForceService.ComputeDelayMs(failedAttempts);
    if (delayMs > 0)
        await Task.Delay(delayMs, httpContext.RequestAborted);

    await using var connection = await db.OpenConnectionAsync();

    // KullanÄ±cÄ± adÄ± veya e-posta ile bul
    var identifier = request.Identifier.ToLowerInvariant();
    await using var findCmd = new NpgsqlCommand(
        """
        SELECT id, username, email, password_hash, email_verified, karma, auth_provider, is_banned,
               is_2fa_enabled, totp_secret
        FROM users
        WHERE (lower(username) = @identifier OR email = @identifier)
          AND auth_provider = 'password'
          AND deleted_at IS NULL
        LIMIT 1
        """,
        connection
    );
    findCmd.Parameters.AddWithValue("identifier", identifier);

    await using var reader = await findCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await bruteForce.RecordFailedAttemptAsync(bfIdentity);
        return BadRequest("INVALID_CREDENTIALS", "KullanÄ±cÄ± adÄ± veya ÅŸifre hatalÄ±.");
    }

    var userId = reader.GetGuid(0);
    var username = reader.GetString(1);
    var email = reader.GetString(2);
    var passwordHash = reader.IsDBNull(3) ? null : reader.GetString(3);
    var emailVerified = reader.GetBoolean(4);
    var karma = reader.GetInt32(5);
    var authProvider = reader.GetString(6);
    var isBanned = reader.GetBoolean(7);
    var is2faEnabled = reader.GetBoolean(8);
    var totpSecret = reader.IsDBNull(9) ? null : reader.GetString(9);
    await reader.CloseAsync();

    if (isBanned)
    {
        return BadRequest("USER_BANNED", "HesabÄ±nÄ±z askÄ±ya alÄ±nmÄ±ÅŸtÄ±r.");
    }

    if (!emailVerified)
    {
        return BadRequest("EMAIL_NOT_VERIFIED", "E-posta adresiniz doÄŸrulanmamÄ±ÅŸ.");
    }

    if (passwordHash is null || !PasswordService.Verify(request.Password, passwordHash))
    {
        await bruteForce.RecordFailedAttemptAsync(bfIdentity);
        return BadRequest("INVALID_CREDENTIALS", "KullanÄ±cÄ± adÄ± veya ÅŸifre hatalÄ±.");
    }

    // BaÅŸarÄ±lÄ± giriÅŸ â€" sayacÄ± temizle
    var backupCodeUsed = false;
    if (is2faEnabled)
    {
        var totpValid = totpSecret is not null
            && request.TotpCode is not null
            && TotpService.Validate(totpSecret, request.TotpCode);
        if (!totpValid)
        {
            if (request.BackupCode is null)
            {
                await bruteForce.RecordFailedAttemptAsync(bfIdentity);
                return BadRequest("TWO_FACTOR_REQUIRED", "GeÃ§erli iki faktÃ¶rlÃ¼ doÄŸrulama kodu veya yedek kod gerekli.");
            }
            backupCodeUsed = true;
        }
    }

    await bruteForce.ClearAsync(bfIdentity);

    await using var transaction = await connection.BeginTransactionAsync();

    if (backupCodeUsed)
    {
        var codeHash = HashBackupCode(request.BackupCode!);
        await using var backupCmd = new NpgsqlCommand(
            "UPDATE totp_backup_codes SET used_at = NOW() WHERE user_id = @userId AND code_hash = @hash AND used_at IS NULL RETURNING id",
            connection, transaction
        );
        backupCmd.Parameters.AddWithValue("userId", userId);
        backupCmd.Parameters.AddWithValue("hash", codeHash);
        if (await backupCmd.ExecuteScalarAsync() is null)
        {
            await transaction.RollbackAsync();
            await bruteForce.RecordFailedAttemptAsync(bfIdentity);
            return BadRequest("INVALID_BACKUP_CODE", "Yedek kod geÃ§ersiz veya daha Ã¶nce kullanÄ±ldÄ±.");
        }
    }

    var accessToken = jwtService.GenerateAccessToken(userId, username);
    var refreshToken = JwtService.GenerateRefreshToken();
    var refreshHash = JwtService.HashRefreshToken(refreshToken);

    await using var insertRefresh = new NpgsqlCommand(
        "INSERT INTO refresh_tokens (user_id, token_hash) VALUES (@userId, @hash)",
        connection,
        transaction
    );
    insertRefresh.Parameters.AddWithValue("userId", userId);
    insertRefresh.Parameters.AddWithValue("hash", refreshHash);
    await insertRefresh.ExecuteNonQueryAsync();

    // CihazÄ± bu kullanÄ±cÄ±yla iliÅŸkilendir
    if (deviceId is not null)
    {
        await using var linkCmd = new NpgsqlCommand(
            "UPDATE devices SET last_seen_at = NOW() WHERE id = @deviceId",
            connection,
            transaction
        );
        linkCmd.Parameters.AddWithValue("deviceId", deviceId.Value);
        await linkCmd.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    _ = complianceLog.LogAsync("login", ip, deviceId, userId, null, "auth");

    return Results.Ok(new AuthTokensResponse(
        userId,
        username,
        accessToken,
        refreshToken,
        DateTimeOffset.UtcNow.AddMinutes(15),
        User: new UserProfile(userId, username, email, karma, authProvider, true)
    ));
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/refresh", async (
    RefreshTokenRequest request,
    Db db,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var tokenHash = JwtService.HashRefreshToken(request.RefreshToken);

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var findCmd = new NpgsqlCommand(
        """
        SELECT rt.id, rt.user_id, rt.expires_at, u.username, u.email, u.karma, u.auth_provider, u.is_banned
        FROM refresh_tokens rt
        JOIN users u ON u.id = rt.user_id
        WHERE rt.token_hash = @hash AND rt.revoked_at IS NULL
        """,
        connection,
        transaction
    );
    findCmd.Parameters.AddWithValue("hash", tokenHash);

    await using var reader = await findCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    var rtId = reader.GetGuid(0);
    var userId = reader.GetGuid(1);
    var expiresAt = reader.GetFieldValue<DateTimeOffset>(2);
    var username = reader.GetString(3);
    var email = reader.GetString(4);
    var karma = reader.GetInt32(5);
    var authProvider = reader.GetString(6);
    var isBanned = reader.GetBoolean(7);
    await reader.CloseAsync();

    if (expiresAt < DateTimeOffset.UtcNow || isBanned)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    // Token rotation: eski iptal et, yeni ver
    await using var revokeCmd = new NpgsqlCommand(
        "UPDATE refresh_tokens SET revoked_at = NOW() WHERE id = @id",
        connection,
        transaction
    );
    revokeCmd.Parameters.AddWithValue("id", rtId);
    await revokeCmd.ExecuteNonQueryAsync();

    var newAccessToken = jwtService.GenerateAccessToken(userId, username);
    var newRefreshToken = JwtService.GenerateRefreshToken();
    var newRefreshHash = JwtService.HashRefreshToken(newRefreshToken);

    await using var insertCmd = new NpgsqlCommand(
        "INSERT INTO refresh_tokens (user_id, token_hash) VALUES (@userId, @hash)",
        connection,
        transaction
    );
    insertCmd.Parameters.AddWithValue("userId", userId);
    insertCmd.Parameters.AddWithValue("hash", newRefreshHash);
    await insertCmd.ExecuteNonQueryAsync();

    await transaction.CommitAsync();

    return Results.Ok(new AuthTokensResponse(
        userId,
        username,
        newAccessToken,
        newRefreshToken,
        DateTimeOffset.UtcNow.AddMinutes(15),
        User: new UserProfile(userId, username, email, karma, authProvider, true)
    ));
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/logout", async (
    RefreshTokenRequest request,
    Db db
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var tokenHash = JwtService.HashRefreshToken(request.RefreshToken);
    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "UPDATE refresh_tokens SET revoked_at = NOW() WHERE token_hash = @hash AND revoked_at IS NULL",
        connection
    );
    cmd.Parameters.AddWithValue("hash", tokenHash);
    await cmd.ExecuteNonQueryAsync();

    return Results.NoContent();
}).RequireRateLimiting("auth-strict");

// â"€â"€ USER PROFILE â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapGet("/api/v1/users/me", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT u.id,
               u.username,
               u.email,
               u.karma,
               u.auth_provider,
               u.email_verified,
               u.created_at,
               u.is_2fa_enabled,
               u.bio,
               u.username_changed_at,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments cm WHERE cm.device_id = u.device_id AND cm.status != 'deleted') AS comment_count
        FROM users u
        WHERE u.id = @id AND u.deleted_at IS NULL
        """,
        connection
    );
    cmd.Parameters.AddWithValue("id", userId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return NotFound("USER_NOT_FOUND", "KullanÄ±cÄ± bulunamadÄ±.");

    return Results.Ok(new UserProfile(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt32(3),
        reader.GetString(4),
        reader.GetBoolean(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        Convert.ToInt32(reader.GetInt64(10)),
        Convert.ToInt32(reader.GetInt64(11)),
        reader.GetBoolean(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)
    ));
});

app.MapPost("/api/v1/users/me/migrate-guest-data", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    if (!httpRequest.Headers.TryGetValue("X-Device-Token", out var tokenValues))
    {
        return Unauthorized();
    }

    var deviceToken = tokenValues.ToString();
    if (string.IsNullOrWhiteSpace(deviceToken))
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var findDevice = new NpgsqlCommand(
        """
        SELECT d.id
        FROM devices d
        JOIN users u ON u.device_id = d.id
        WHERE d.device_token = @deviceToken
          AND u.id = @userId
          AND u.deleted_at IS NULL
        """,
        connection,
        transaction
    );
    findDevice.Parameters.AddWithValue("deviceToken", deviceToken);
    findDevice.Parameters.AddWithValue("userId", userId);

    var deviceResult = await findDevice.ExecuteScalarAsync();
    if (deviceResult is not Guid deviceId)
    {
        await transaction.RollbackAsync();
        return Unauthorized();
    }

    await using var updatePosts = new NpgsqlCommand(
        """
        UPDATE posts
        SET user_id = @userId,
            updated_at = NOW()
        WHERE device_id = @deviceId
          AND user_id IS NULL
        """,
        connection,
        transaction
    );
    updatePosts.Parameters.AddWithValue("userId", userId);
    updatePosts.Parameters.AddWithValue("deviceId", deviceId);
    await updatePosts.ExecuteNonQueryAsync();

    await using var updateComments = new NpgsqlCommand(
        """
        UPDATE comments
        SET user_id = @userId,
            updated_at = NOW()
        WHERE device_id = @deviceId
          AND user_id IS NULL
        """,
        connection,
        transaction
    );
    updateComments.Parameters.AddWithValue("userId", userId);
    updateComments.Parameters.AddWithValue("deviceId", deviceId);
    await updateComments.ExecuteNonQueryAsync();

    await using var updateVotes = new NpgsqlCommand(
        """
        UPDATE votes
        SET user_id = @userId,
            updated_at = NOW()
        WHERE device_id = @deviceId
          AND user_id IS NULL
        """,
        connection,
        transaction
    );
    updateVotes.Parameters.AddWithValue("userId", userId);
    updateVotes.Parameters.AddWithValue("deviceId", deviceId);
    await updateVotes.ExecuteNonQueryAsync();

    await using var updateReports = new NpgsqlCommand(
        """
        UPDATE reports
        SET reporter_user_id = @userId
        WHERE reporter_device_id = @deviceId
          AND reporter_user_id IS NULL
        """,
        connection,
        transaction
    );
    updateReports.Parameters.AddWithValue("userId", userId);
    updateReports.Parameters.AddWithValue("deviceId", deviceId);
    await updateReports.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/users/username-availability", async (
    string username,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    username = username.Trim().ToLowerInvariant();
    if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
    {
        return BadRequest("VALIDATION_ERROR", "KullanÄ±cÄ± adÄ± 3-20 karakter, yalnÄ±zca kÃ¼Ã§Ã¼k harf/rakam/alt Ã§izgi.");
    }

    var currentUserId = GetJwtPrincipal(httpRequest, jwtService) is { } principal
        ? GetUserId(principal)
        : (Guid?)null;

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT id
        FROM users
        WHERE lower(username) = @username AND deleted_at IS NULL
        LIMIT 1
        """,
        connection
    );
    command.Parameters.AddWithValue("username", username);

    var existingId = await command.ExecuteScalarAsync();
    var available = existingId is null
        || existingId is DBNull
        || (currentUserId.HasValue && (Guid)existingId == currentUserId.Value);

    return Results.Ok(new { username, available });
});

app.MapPut("/api/v1/users/me", async (
    JsonElement request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var username = request.TryGetProperty("username", out var usernameElement)
        ? usernameElement.GetString()?.Trim().ToLowerInvariant()
        : null;
    var bio = request.TryGetProperty("bio", out var bioElement)
        ? bioElement.GetString()?.Trim()
        : null;
    if (bio == "") bio = null;

    if (username is not null && !Regex.IsMatch(username, "^[a-z0-9_]{3,20}$"))
    {
        return BadRequest("VALIDATION_ERROR", "Kullanıcı adı 3-20 karakter, yalnızca küçük harf/rakam/alt çizgi.");
    }
    if (bio is { Length: > 150 })
    {
        return BadRequest("VALIDATION_ERROR", "Biyografi en fazla 150 karakter olabilir.");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var current = new NpgsqlCommand(
        "SELECT username, username_changed_at FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    current.Parameters.AddWithValue("id", userId);
    await using var currentReader = await current.ExecuteReaderAsync();
    if (!await currentReader.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");
    var currentUsername = currentReader.GetString(0);
    var usernameChangedAt = currentReader.IsDBNull(1)
        ? (DateTimeOffset?)null
        : currentReader.GetFieldValue<DateTimeOffset>(1);
    await currentReader.CloseAsync();

    username ??= currentUsername;
    var isUsernameChanged = !string.Equals(currentUsername, username, StringComparison.OrdinalIgnoreCase);
    if (isUsernameChanged &&
        usernameChangedAt is not null &&
        DateTimeOffset.UtcNow - usernameChangedAt.Value < TimeSpan.FromDays(30))
    {
        return BadRequest("USERNAME_CHANGE_LOCKED", "Kullanıcı adını 30 günde bir değiştirebilirsin.");
    }

    await using var existing = new NpgsqlCommand(
        """
        SELECT id
        FROM users
        WHERE lower(username) = @username
          AND id != @id
          AND deleted_at IS NULL
        LIMIT 1
        """,
        connection
    );
    existing.Parameters.AddWithValue("id", userId);
    existing.Parameters.AddWithValue("username", username);
    if (await existing.ExecuteScalarAsync() is not null)
    {
        return Conflict("USERNAME_TAKEN", "Bu kullanÄ±cÄ± adÄ± zaten alÄ±nmÄ±ÅŸ.");
    }

    await using var command = new NpgsqlCommand(
        """
        WITH updated_user AS (
            UPDATE users
            SET username = @username,
                bio = @bio,
                username_changed_at = CASE WHEN @isUsernameChanged THEN NOW() ELSE username_changed_at END,
                updated_at = NOW()
            WHERE id = @id AND deleted_at IS NULL
            RETURNING id, device_id, username, email, karma, auth_provider, email_verified, created_at,
                      bio, username_changed_at
        )
        SELECT u.id,
               u.username,
               u.email,
               u.karma,
               u.auth_provider,
               u.email_verified,
               u.created_at,
               u.is_2fa_enabled,
               u.bio,
               u.username_changed_at,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments cm WHERE cm.device_id = u.device_id AND cm.status != 'deleted') AS comment_count
        FROM updated_user u
        """,
        connection
    );
    command.Parameters.AddWithValue("id", userId);
    command.Parameters.AddWithValue("username", username);
    command.Parameters.AddWithValue("isUsernameChanged", isUsernameChanged);
    command.Parameters.Add("bio", NpgsqlDbType.Text).Value = (object?)bio ?? DBNull.Value;

    try
    {
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return NotFound("USER_NOT_FOUND", "KullanÄ±cÄ± bulunamadÄ±.");

        return Results.Ok(new UserProfile(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            Convert.ToInt32(reader.GetInt64(10)),
            Convert.ToInt32(reader.GetInt64(11)),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)
        ));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Conflict("USERNAME_TAKEN", "Bu kullanÄ±cÄ± adÄ± zaten alÄ±nmÄ±ÅŸ.");
    }
});

app.MapPut("/api/v1/users/me/notification-preferences", async (
    NotificationPreferencesRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE users SET notification_preferences = @prefs, updated_at = NOW() WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    command.Parameters.AddWithValue("id", userId);
    command.Parameters.Add("prefs", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(request);
    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0 ? NotFound("USER_NOT_FOUND", "KullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.") : Results.NoContent();
});

app.MapGet("/api/v1/users/me/notification-preferences", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "SELECT notification_preferences FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    command.Parameters.AddWithValue("id", userId);

    var preferencesJson = await command.ExecuteScalarAsync() as string;
    if (preferencesJson is null)
    {
        return NotFound("USER_NOT_FOUND", "KullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.");
    }

    NotificationPreferencesRequest preferences;
    try
    {
        preferences = JsonSerializer.Deserialize<NotificationPreferencesRequest>(preferencesJson)
            ?? new NotificationPreferencesRequest(null, null, null, null, null, null, null);
    }
    catch (JsonException)
    {
        preferences = new NotificationPreferencesRequest(null, null, null, null, null, null, null);
    }

    return Results.Ok(preferences);
});

// Politika versiyonu sabitleri — büyük politika değişikliklerinde bu değerler artırılır.
const int CurrentTermsVersion = 1;
const int CurrentPrivacyVersion = 1;

app.MapGet("/api/v1/users/me/policy-status", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT terms_version_accepted, privacy_version_accepted FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    cmd.Parameters.AddWithValue("id", userId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    var termsAccepted = (int)reader.GetInt16(0);
    var privacyAccepted = (int)reader.GetInt16(1);
    var needsAcceptance = termsAccepted < CurrentTermsVersion || privacyAccepted < CurrentPrivacyVersion;

    return Results.Ok(new PolicyStatusResponse(needsAcceptance, CurrentTermsVersion, CurrentPrivacyVersion));
});

app.MapPost("/api/v1/users/me/accept-policy", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    AcceptPolicyRequest req
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    if (req.TermsVersion < CurrentTermsVersion || req.PrivacyVersion < CurrentPrivacyVersion)
        return BadRequest("INVALID_POLICY_VERSION", "Geçerli politika versiyonunu kabul etmeniz gerekmektedir.");

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE users
           SET terms_version_accepted   = @tv,
               privacy_version_accepted = @pv
         WHERE id = @id AND deleted_at IS NULL
        """,
        connection
    );
    cmd.Parameters.AddWithValue("id", userId);
    cmd.Parameters.AddWithValue("tv", (short)req.TermsVersion);
    cmd.Parameters.AddWithValue("pv", (short)req.PrivacyVersion);

    var rows = await cmd.ExecuteNonQueryAsync();
    if (rows == 0) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    return Results.Ok(new MessageResponse("Politika kabulü kaydedildi."));
});

app.MapGet("/api/v1/users/me/data-export", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    RedisService redis
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);
    var isAllowed = await redis.IsAllowedAsync(
        "data-export",
        userId.ToString("N"),
        limit: 1,
        window: TimeSpan.FromDays(1));
    if (!isAllowed)
    {
        return TooManyRequests(
            "DATA_EXPORT_LIMIT",
            "Veri dışa aktarma işlemini günde bir kez yapabilirsiniz.",
            retryAfterSeconds: 86_400);
    }

    await using var connection = await db.OpenConnectionAsync();

    static void ApplyDataExportBudget(NpgsqlCommand command)
    {
        command.CommandTimeout = 10;
    }

    await using var userCmd = new NpgsqlCommand(
        "SELECT username, email, karma, created_at FROM users WHERE id = @id AND deleted_at IS NULL",
        connection);
    ApplyDataExportBudget(userCmd);
    userCmd.Parameters.AddWithValue("id", userId);
    await using var userReader = await userCmd.ExecuteReaderAsync();
    if (!await userReader.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    var userData = new
    {
        username = userReader.GetString(0),
        email = userReader.GetString(1),
        karma = userReader.GetInt32(2),
        joinedAt = userReader.GetFieldValue<DateTimeOffset>(3)
    };
    await userReader.CloseAsync();

    await using var postsCmd = new NpgsqlCommand(
        "SELECT title, content, created_at, status FROM posts WHERE user_id = @id ORDER BY created_at DESC",
        connection);
    ApplyDataExportBudget(postsCmd);
    postsCmd.Parameters.AddWithValue("id", userId);
    var posts = new List<object>();
    await using (var reader = await postsCmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            posts.Add(new { title = reader.GetString(0), content = reader.GetString(1), createdAt = reader.GetFieldValue<DateTimeOffset>(2), status = reader.GetString(3) });
        }
    }

    await using var commentsCmd = new NpgsqlCommand(
        "SELECT content, created_at, status FROM comments WHERE user_id = @id ORDER BY created_at DESC",
        connection);
    ApplyDataExportBudget(commentsCmd);
    commentsCmd.Parameters.AddWithValue("id", userId);
    var comments = new List<object>();
    await using (var reader = await commentsCmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            comments.Add(new { content = reader.GetString(0), createdAt = reader.GetFieldValue<DateTimeOffset>(1), status = reader.GetString(2) });
        }
    }

    var exportedAt = DateTimeOffset.UtcNow;
    var exportData = new
    {
        user = userData,
        posts = posts,
        comments = comments,
        exportedAt = exportedAt,
        notice = "Bu veriler KVKK/GDPR kapsamında talebiniz üzerine üretilmiştir."
    };

    // BTK/KVKK: JSON bytes + SHA-256 hash + HMAC-SHA256 imzası (hash zinciri)
    var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(exportData);
    var sha256Hex = Convert.ToHexString(SHA256.HashData(jsonBytes)).ToLowerInvariant();

    var chainRoot = $"{userId:N}:{exportedAt:O}:{sha256Hex}";
    using var hmac = new HMACSHA256(jwtService.GetHmacSigningBytes());
    var hmacHex = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(chainRoot))).ToLowerInvariant();

    var envelope = new
    {
        schemaVersion = "1.0",
        exportData,
        integrity = new
        {
            sha256 = sha256Hex,
            hmacSha256 = hmacHex,
            hashChainInput = chainRoot,
            algorithm = "HMAC-SHA256 with server signing key"
        }
    };

    var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions { WriteIndented = true });
    var fileName = $"karar-veri-disa-aktarma-{exportedAt:yyyyMMddHHmmss}.json";

    return Results.File(envelopeBytes, "application/json", fileName);
});

app.MapGet("/api/v1/users/me/moderation-history", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    var userDeviceId = await GetUserDeviceIdAsync(connection, userId);
    if (userDeviceId is null) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    await using var summaryCmd = new NpgsqlCommand(
        """
        SELECT
            (SELECT COUNT(*) FROM posts WHERE user_id = @userId AND status = 'active') AS active_posts,
            (
                SELECT COUNT(*) FROM posts
                WHERE user_id = @userId
                  AND status IN ('auto_hidden', 'deleted', 'under_review')
            ) AS removed_posts,
            (SELECT COUNT(*) FROM user_strikes WHERE user_id = @userId) AS warnings
        """,
        connection
    );
    summaryCmd.Parameters.AddWithValue("userId", userId);
    await using var summaryReader = await summaryCmd.ExecuteReaderAsync();
    await summaryReader.ReadAsync();
    var activePosts = Convert.ToInt32(summaryReader.GetInt64(0));
    var removedPosts = Convert.ToInt32(summaryReader.GetInt64(1));
    var warnings = Convert.ToInt32(summaryReader.GetInt64(2));
    await summaryReader.CloseAsync();

    await using var eventsCmd = new NpgsqlCommand(
        """
        SELECT *
        FROM (
            SELECT p.id,
                   'post' AS target_type,
                   CASE
                       WHEN p.status = 'under_review' THEN 'review'
                       ELSE 'removed'
                   END AS action,
                   COALESCE(NULLIF(p.moderation_reason, ''), 'Topluluk kuralları') AS reason,
                   LEFT(p.title, 140) AS content_excerpt,
                   COALESCE(p.moderation_checked_at, p.updated_at, p.created_at) AS created_at,
                   COALESCE(a.status, 'none') AS appeal_status
            FROM posts p
            LEFT JOIN moderation_appeals a
              ON a.user_id = @userId
             AND a.target_type = 'post'
             AND a.target_id = p.id
            WHERE p.user_id = @userId
              AND p.status IN ('under_review', 'auto_hidden', 'deleted')

            UNION ALL

            SELECT c.id,
                   'comment' AS target_type,
                   CASE
                       WHEN c.status = 'under_review' THEN 'review'
                       ELSE 'removed'
                   END AS action,
                   COALESCE(NULLIF(c.moderation_reason, ''), 'Topluluk kuralları') AS reason,
                   LEFT(c.content, 140) AS content_excerpt,
                   COALESCE(c.moderation_checked_at, c.updated_at, c.created_at) AS created_at,
                   COALESCE(a.status, 'none') AS appeal_status
            FROM comments c
            LEFT JOIN moderation_appeals a
              ON a.user_id = @userId
             AND a.target_type = 'comment'
             AND a.target_id = c.id
            WHERE c.user_id = @userId
              AND c.status IN ('under_review', 'auto_hidden', 'deleted')

            UNION ALL

            SELECT s.id,
                   'user' AS target_type,
                   CASE
                       WHEN s.severity = 'light' THEN 'warning'
                       ELSE 'strike'
                   END AS action,
                   s.reason,
                   s.note AS content_excerpt,
                   s.created_at,
                   'none' AS appeal_status
            FROM user_strikes s
            WHERE s.user_id = @userId
        ) events
        ORDER BY created_at DESC
        LIMIT 50
        """,
        connection
    );
    eventsCmd.Parameters.AddWithValue("userId", userId);
    eventsCmd.Parameters.AddWithValue("deviceId", userDeviceId.Value);

    var events = new List<object>();
    await using var eventsReader = await eventsCmd.ExecuteReaderAsync();
    while (await eventsReader.ReadAsync())
    {
        events.Add(new
        {
            id = eventsReader.GetGuid(0),
            targetType = eventsReader.GetString(1),
            targetId = eventsReader.GetGuid(0),
            action = eventsReader.GetString(2),
            reason = eventsReader.GetString(3),
            contentExcerpt = eventsReader.IsDBNull(4) ? null : eventsReader.GetString(4),
            createdAt = eventsReader.GetFieldValue<DateTimeOffset>(5),
            appealStatus = eventsReader.GetString(6)
        });
    }

    return Results.Ok(new
    {
        activePosts,
        removedPosts,
        warnings,
        events
    });
});

app.MapGet("/api/v1/users/me/reports", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    int page = 1,
    int limit = 20
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId);
    if (effectiveDeviceId is null) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    await using var countCmd = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM reports r
        WHERE r.reporter_user_id = @userId OR r.reporter_device_id = @deviceId
        """,
        connection
    );
    countCmd.Parameters.AddWithValue("userId", userId);
    countCmd.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        """
        SELECT r.id,
               r.target_type,
               r.target_id,
               r.reason,
               r.status,
               r.created_at,
               CASE
                   WHEN r.target_type = 'post' THEN LEFT(COALESCE(p.title, p.content), 140)
                   WHEN r.target_type = 'comment' THEN LEFT(c.content, 140)
               END AS target_preview,
               CASE r.status
                   WHEN 'pending'      THEN 'alındı'
                   WHEN 'under_review' THEN 'inceleniyor'
                   WHEN 'actioned'     THEN 'işlem yapıldı'
                   WHEN 'dismissed'    THEN 'reddedildi'
                   ELSE 'alındı'
               END AS public_status,
               CASE r.status
                   WHEN 'dismissed' THEN 'İnceleme sonunda bu içerikte işlem gerektiren bir ihlal bulunmadı.'
                   ELSE NULL
               END AS public_reason
        FROM reports r
        LEFT JOIN posts p ON r.target_type = 'post' AND p.id = r.target_id
        LEFT JOIN comments c ON r.target_type = 'comment' AND c.id = r.target_id
        WHERE r.reporter_user_id = @userId OR r.reporter_device_id = @deviceId
        ORDER BY r.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("deviceId", effectiveDeviceId.Value);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var reports = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        reports.Add(new
        {
            id = reader.GetGuid(0),
            targetType = reader.GetString(1),
            targetId = reader.GetGuid(2),
            reason = reader.GetString(3),
            status = reader.GetString(4),
            createdAt = reader.GetFieldValue<DateTimeOffset>(5),
            targetPreview = reader.IsDBNull(6) ? null : reader.GetString(6),
            publicStatus = reader.GetString(7),
            publicReason = reader.IsDBNull(8) ? null : reader.GetString(8)
        });
    }

    return Results.Ok(new
    {
        reports,
        pagination = new Pagination(page, limit, total, offset + reports.Count < total)
    });
});

app.MapPost("/api/v1/users/me/moderation-appeals", async (
    CreateModerationAppealRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    if (ValidateRequest(request) is { } validationError) return validationError;
    if (request.TargetType is not ("post" or "comment"))
    {
        return BadRequest("INVALID_TARGET_TYPE", "İtiraz hedefi post veya comment olmalı.");
    }

    var userId = GetUserId(principal);
    var message = request.Message.Trim();

    await using var connection = await db.OpenConnectionAsync();
    var userDeviceId = await GetUserDeviceIdAsync(connection, userId);
    if (userDeviceId is null) return NotFound("USER_NOT_FOUND", "Kullanıcı bulunamadı.");

    var table = request.TargetType == "post" ? "posts" : "comments";
    await using var ownershipCmd = new NpgsqlCommand(
        $"""
        SELECT status
        FROM {table}
        WHERE id = @targetId
          AND (user_id = @userId OR device_id = @deviceId)
          AND status IN ('auto_hidden', 'deleted')
        """,
        connection
    );
    ownershipCmd.Parameters.AddWithValue("targetId", request.TargetId);
    ownershipCmd.Parameters.AddWithValue("userId", userId);
    ownershipCmd.Parameters.AddWithValue("deviceId", userDeviceId.Value);
    var status = await ownershipCmd.ExecuteScalarAsync();
    if (status is null)
    {
        return NotFound("APPEAL_TARGET_NOT_FOUND", "İtiraz edilebilir kaldırılmış içerik bulunamadı.");
    }

    await using var command = new NpgsqlCommand(
        """
        INSERT INTO moderation_appeals (user_id, target_type, target_id, message)
        VALUES (@userId, @targetType, @targetId, @message)
        RETURNING id, created_at
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("targetType", request.TargetType);
    command.Parameters.AddWithValue("targetId", request.TargetId);
    command.Parameters.AddWithValue("message", message);

    try
    {
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Results.Created(
            $"/api/v1/users/me/moderation-appeals/{reader.GetGuid(0)}",
            new
            {
                id = reader.GetGuid(0),
                status = "pending",
                createdAt = reader.GetFieldValue<DateTimeOffset>(1),
                message = "İtirazın alındı. İnceleme sonucu moderasyon geçmişinde görünecek."
            }
        );
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Conflict("APPEAL_ALREADY_EXISTS", "Bu içerik için daha önce itiraz gönderdin.");
    }
});

app.MapPost("/api/v1/users/me/blocked", async (
    UserIdRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);
    if (userId == request.UserId) return BadRequest("CANNOT_BLOCK_SELF", "Kendi hesabÃ„Â±nÃ„Â±zÃ„Â± engelleyemezsiniz.");

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO blocked_users (blocker_user_id, blocked_user_id)
        SELECT @blockerId, @blockedId
        WHERE EXISTS (SELECT 1 FROM users WHERE id = @blockedId AND deleted_at IS NULL)
        ON CONFLICT DO NOTHING
        """,
        connection
    );
    command.Parameters.AddWithValue("blockerId", userId);
    command.Parameters.AddWithValue("blockedId", request.UserId);
    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0 ? NotFound("USER_NOT_FOUND", "Engellenecek kullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.") : Results.NoContent();
});

app.MapDelete("/api/v1/users/me/blocked/{blockedId:guid}", async (
    Guid blockedId,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "DELETE FROM blocked_users WHERE blocker_user_id = @blockerId AND blocked_user_id = @blockedId",
        connection
    );
    command.Parameters.AddWithValue("blockerId", userId);
    command.Parameters.AddWithValue("blockedId", blockedId);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/users/me/blocked", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT bu.blocked_user_id,
               COALESCE(u.username, 'silinmis_kullanici') AS username,
               bu.created_at
        FROM blocked_users bu
        LEFT JOIN users u ON u.id = bu.blocked_user_id
        WHERE bu.blocker_user_id = @userId
        ORDER BY bu.created_at DESC
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    var users = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            id = reader.GetGuid(0),
            username = reader.GetString(1),
            blockedAt = reader.GetFieldValue<DateTimeOffset>(2)
        });
    }
    return Results.Ok(users);
});

app.MapGet("/api/v1/users/{username}/profile", async (
    string username,
    Db db
) =>
{
    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT u.id,
               u.username,
               u.karma,
               u.auth_provider,
               u.email_verified,
               u.created_at,
               u.bio,
               u.username_changed_at,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status != 'deleted') AS post_count,
               (SELECT COUNT(*) FROM comments cm WHERE cm.user_id = u.id AND cm.status != 'deleted') AS comment_count
        FROM users u
        WHERE lower(u.username) = lower(@username) AND u.deleted_at IS NULL
        """,
        connection
    );
    command.Parameters.AddWithValue("username", username);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return NotFound("USER_NOT_FOUND", "KullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.");

    return Results.Ok(new UserProfile(
        reader.GetGuid(0),
        reader.GetString(1),
        "",
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetBoolean(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        Convert.ToInt32(reader.GetInt64(8)),
        Convert.ToInt32(reader.GetInt64(9)),
        false,
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)
    ));
});

app.MapPost("/api/v1/auth/2fa/setup", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);
    var username = principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value ?? "karar";
    var secret = GenerateBase32Secret();
    var otpAuthUrl = $"otpauth://totp/Karar:{Uri.EscapeDataString(username)}?secret={secret}&issuer=Karar&digits=6&period=30";

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE users SET totp_secret = @secret, updated_at = NOW() WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    command.Parameters.AddWithValue("id", userId);
    command.Parameters.AddWithValue("secret", secret);
    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0
        ? NotFound("USER_NOT_FOUND", "KullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.")
        : Results.Ok(new TwoFactorSetupResponse(secret, otpAuthUrl));
});

app.MapPost("/api/v1/auth/2fa/enable", async (
    TwoFactorCodeRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError) return validationError;
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var select = new NpgsqlCommand(
        "SELECT totp_secret FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    select.Parameters.AddWithValue("id", userId);
    var secret = await select.ExecuteScalarAsync() as string;
    if (secret is null || !TotpService.Validate(secret, request.Code))
    {
        return BadRequest("INVALID_TOTP_CODE", "DoÃ„Å¸rulama kodu hatalÃ„Â±.");
    }

    await using var update = new NpgsqlCommand(
        "UPDATE users SET is_2fa_enabled = TRUE, updated_at = NOW() WHERE id = @id",
        connection
    );
    update.Parameters.AddWithValue("id", userId);
    await update.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapPost("/api/v1/auth/2fa/disable", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE users SET is_2fa_enabled = FALSE, totp_secret = NULL, updated_at = NOW() WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    command.Parameters.AddWithValue("id", userId);
    var affected = await command.ExecuteNonQueryAsync();
    return affected == 0 ? NotFound("USER_NOT_FOUND", "KullanÃ„Â±cÃ„Â± bulunamadÃ„Â±.") : Results.NoContent();
});

app.MapGet("/api/v1/users/me/sessions", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT rt.id, d.platform, d.last_seen_at
        FROM refresh_tokens rt
        JOIN users u ON u.id = rt.user_id
        JOIN devices d ON d.id = u.device_id
        WHERE rt.user_id = @userId AND rt.revoked_at IS NULL AND rt.expires_at > NOW()
        ORDER BY rt.created_at DESC
        """,
        connection
    );
    command.Parameters.AddWithValue("userId", userId);
    var sessions = new List<UserSessionDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        sessions.Add(new UserSessionDto(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), false));
    }
    return Results.Ok(sessions);
});

app.MapDelete("/api/v1/users/me/sessions/{sessionId:guid}", async (
    Guid sessionId,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        "UPDATE refresh_tokens SET revoked_at = NOW() WHERE id = @id AND user_id = @userId",
        connection
    );
    command.Parameters.AddWithValue("id", sessionId);
    command.Parameters.AddWithValue("userId", userId);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapDelete("/api/v1/users/me", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    EmailService emailService,
    IConfiguration configuration
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);
    var request = await ReadOptionalDeleteAccountRequestAsync(httpRequest);
    if (request is not null && ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var findCommand = new NpgsqlCommand(
        """
        SELECT device_id, auth_provider, password_hash, email, username
        FROM users
        WHERE id = @userId AND deleted_at IS NULL
        """,
        connection,
        transaction
    );
    findCommand.Parameters.AddWithValue("userId", userId);
    await using var reader = await findCommand.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    }
    var deviceId = reader.GetGuid(0);
    var authProvider = reader.GetString(1);
    var passwordHash = reader.IsDBNull(2) ? null : reader.GetString(2);
    var userEmail = reader.GetString(3);
    var userUsername = reader.GetString(4);
    await reader.CloseAsync();

    if (authProvider == "password")
    {
        if (string.IsNullOrWhiteSpace(request?.Password))
        {
            await transaction.RollbackAsync();
            return BadRequest("PASSWORD_REQUIRED", "Hesap silmek icin sifrenizi tekrar girin.");
        }

        if (passwordHash is null || !PasswordService.Verify(request.Password, passwordHash))
        {
            await transaction.RollbackAsync();
            return BadRequest("INVALID_PASSWORD", "Sifre hatali.");
        }
    }

    await using var deleteUser = new NpgsqlCommand(
        """
        UPDATE users
        SET deleted_at = NOW(),
            updated_at = NOW()
        WHERE id = @userId AND deleted_at IS NULL
        """,
        connection,
        transaction
    );
    deleteUser.Parameters.AddWithValue("userId", userId);
    await deleteUser.ExecuteNonQueryAsync();

    await using var revokeTokens = new NpgsqlCommand(
        """
        UPDATE refresh_tokens
        SET revoked_at = NOW()
        WHERE user_id = @userId AND revoked_at IS NULL
        """,
        connection,
        transaction
    );
    revokeTokens.Parameters.AddWithValue("userId", userId);
    await revokeTokens.ExecuteNonQueryAsync();

    await using var deleteFcmTokens = new NpgsqlCommand(
        "DELETE FROM fcm_tokens WHERE device_id = @deviceId",
        connection,
        transaction
    );
    deleteFcmTokens.Parameters.AddWithValue("deviceId", deviceId);
    await deleteFcmTokens.ExecuteNonQueryAsync();

    await using var deactivateDevice = new NpgsqlCommand(
        """
        UPDATE devices
        SET device_token = @deviceToken,
            last_seen_at = NOW()
        WHERE id = @deviceId
        """,
        connection,
        transaction
    );
    deactivateDevice.Parameters.AddWithValue("deviceId", deviceId);
    deactivateDevice.Parameters.AddWithValue("deviceToken", $"deleted:{deviceId:N}:{Guid.NewGuid():N}");
    await deactivateDevice.ExecuteNonQueryAsync();

    // Hesap kurtarma tokeni oluÅŸtur
    var recoveryRaw = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
    var recoveryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recoveryRaw)));
    await using var recoveryCmd = new NpgsqlCommand(
        "INSERT INTO account_recovery_tokens (user_id, token_hash, expires_at) VALUES (@userId, @hash, @expiresAt)",
        connection, transaction
    );
    recoveryCmd.Parameters.AddWithValue("userId", userId);
    recoveryCmd.Parameters.AddWithValue("hash", recoveryHash);
    recoveryCmd.Parameters.AddWithValue("expiresAt", DateTimeOffset.UtcNow.AddDays(30));
    await recoveryCmd.ExecuteNonQueryAsync();

    await transaction.CommitAsync();

    var webBase = GetWebBaseUrl(configuration);
    _ = emailService.SendAccountRecoveryAsync(userEmail, userUsername,
        $"{webBase}/recover-account?token={recoveryRaw}");

    return Results.NoContent();
});

// â"€â"€ PUBLIC USER POSTS â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapGet("/api/v1/users/{username}/posts", async (
    string username,
    Db db,
    RequestDevice requestDevice,
    HttpRequest httpRequest,
    JwtService jwtService,
    int page = 1,
    int limit = 20,
    string sort = "new"
) =>
{
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var deviceParam = deviceId ?? Guid.Empty;
    var userId = GetOptionalUserId(httpRequest, jwtService);
    var userParam = userId ?? Guid.Empty;
    var orderBy = sort switch
    {
        "old" => "p.created_at ASC",
        "votes" => "p.vote_count_hakli + p.vote_count_haksiz DESC, p.created_at DESC",
        "comments" => "p.comment_count DESC, p.created_at DESC",
        _ => "p.created_at DESC"
    };

    await using var connection = await db.OpenConnectionAsync();

    await using var ownerCmd = new NpgsqlCommand(
        "SELECT id FROM users WHERE lower(username) = lower(@username) AND deleted_at IS NULL",
        connection
    );
    ownerCmd.Parameters.AddWithValue("username", username);
    var ownerId = await ownerCmd.ExecuteScalarAsync();
    if (ownerId is null) return NotFound("USER_NOT_FOUND", "KullanÄ±cÄ± bulunamadÄ±.");

    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM posts WHERE user_id = @ownerId AND status = 'active'",
        connection
    );
    countCmd.Parameters.AddWithValue("ownerId", ownerId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        $"""
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.user_id = @userParam), p.is_edited,
               EXISTS(SELECT 1 FROM saved_posts sp WHERE sp.post_id = p.id AND sp.device_id = @deviceParam),
               CASE WHEN p.is_anonymous THEN NULL ELSE u.username END AS username,
               p.tags, p.is_anonymous
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        JOIN users u ON u.id = p.user_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceParam
        WHERE p.user_id = @ownerId AND p.status = 'active'
        ORDER BY {orderBy}
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("ownerId", ownerId);
    cmd.Parameters.AddWithValue("deviceParam", deviceParam);
    cmd.Parameters.AddWithValue("userParam", userParam);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var posts = await ReadPostsAsync(cmd);
    return Results.Ok(new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total)));
});

// â"€â"€ MY COMMENTS â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapGet("/api/v1/users/me/comments", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    int page = 1,
    int limit = 20
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM comments WHERE user_id = @userId AND status = 'active'",
        connection
    );
    countCmd.Parameters.AddWithValue("userId", userId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT cm.id, cm.content, cm.upvote_count, cm.downvote_count, cm.is_edited, cm.is_pinned,
               cm.created_at, p.id, p.title
        FROM comments cm
        JOIN posts p ON p.id = cm.post_id
        WHERE cm.user_id = @userId AND cm.status = 'active'
        ORDER BY cm.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var items = new List<MyCommentDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new MyCommentDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            reader.GetString(8)
        ));
    }

    return Results.Ok(new { Comments = items, Pagination = new Pagination(page, limit, total, offset + items.Count < total) });
});

app.MapGet("/api/v1/users/{username}/comments", async (
    string username,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    int page = 1,
    int limit = 20
) =>
{
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    var userId = principal is null ? null : (Guid?)GetUserId(principal);
    var deviceParam = deviceId ?? Guid.Empty;

    await using var connection = await db.OpenConnectionAsync();

    // Find user by username
    await using var userCmd = new NpgsqlCommand(
        "SELECT id, is_profile_visible FROM users WHERE username = @username AND deleted_at IS NULL",
        connection
    );
    userCmd.Parameters.AddWithValue("username", username);
    await using var userReader = await userCmd.ExecuteReaderAsync();
    if (!await userReader.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    var targetUserId = userReader.GetGuid(0);
    var isVisible = userReader.GetBoolean(1);
    await userReader.CloseAsync();

    if (!isVisible && targetUserId != userId)
        return Forbid("PRIVATE_PROFILE", "Bu profil gizli.");

    await using var countCmd = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM comments cm
        JOIN posts p ON p.id = cm.post_id
        WHERE cm.user_id = @userId AND cm.status = 'active' AND p.status = 'active'
        """,
        connection
    );
    countCmd.Parameters.AddWithValue("userId", targetUserId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    // We reuse a similar query as my-comments but with more details if needed
    await using var cmd = new NpgsqlCommand(
        $"""
        SELECT cm.id, cm.content, cm.upvote_count, cm.downvote_count, cm.is_edited, cm.is_pinned,
               cm.created_at, p.id, p.title
        FROM comments cm
        JOIN posts p ON p.id = cm.post_id
        WHERE cm.user_id = @userId AND cm.status = 'active' AND p.status = 'active'
        ORDER BY cm.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", targetUserId);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var items = new List<MyCommentDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new MyCommentDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetGuid(7),
            reader.GetString(8)
        ));
    }

    return Results.Ok(new { Comments = items, Pagination = new Pagination(page, limit, total, offset + items.Count < total) });
});

app.MapGet("/api/v1/users/me/karma-history", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    int page = 1,
    int limit = 30
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;

    await using var connection = await db.OpenConnectionAsync();
    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM karma_milestones WHERE user_id = @userId",
        connection
    );
    countCmd.Parameters.AddWithValue("userId", userId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT id, source_type, source_id, milestone, karma_delta, created_at
        FROM karma_milestones
        WHERE user_id = @userId
        ORDER BY created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var items = new List<KarmaHistoryDto>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new KarmaHistoryDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5)
        ));
    }

    return Results.Ok(new { Items = items, Pagination = new Pagination(page, limit, total, offset + items.Count < total) });
});

app.MapGet("/api/v1/users/me/weekly-stats", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var userCmd = new NpgsqlCommand(
        "SELECT device_id FROM users WHERE id = @userId AND deleted_at IS NULL",
        connection
    );
    userCmd.Parameters.AddWithValue("userId", userId);
    var deviceResult = await userCmd.ExecuteScalarAsync();
    if (deviceResult is not Guid deviceId) return Unauthorized();

    var today = DateTimeOffset.UtcNow.Date;
    var weekStart = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
    var weekEndExclusive = weekStart.AddDays(7);
    var weekLabel = $"{weekStart:yyyy-MM-dd} - {weekEndExclusive.AddDays(-1):yyyy-MM-dd}";

    await using var statsCmd = new NpgsqlCommand(
        """
        SELECT
            COALESCE((SELECT SUM(karma_delta)::int
                      FROM karma_milestones
                      WHERE user_id = @userId
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS karma_earned,
            COALESCE((SELECT COUNT(*)::int
                      FROM votes
                      WHERE device_id = @deviceId
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS votes_given,
            COALESCE((SELECT COUNT(*)::int
                      FROM votes
                      WHERE device_id = @deviceId
                        AND vote_type = 'hakli'
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS hakli_given,
            COALESCE((SELECT COUNT(*)::int
                      FROM votes
                      WHERE device_id = @deviceId
                        AND vote_type = 'haksiz'
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS haksiz_given,
            COALESCE((SELECT COUNT(*)::int
                      FROM posts
                      WHERE user_id = @userId
                        AND status != 'deleted'
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS posts_created,
            COALESCE((SELECT COUNT(*)::int
                      FROM comments
                      WHERE user_id = @userId
                        AND status != 'deleted'
                        AND created_at >= @weekStart
                        AND created_at < @weekEnd), 0) AS comments_posted
        """,
        connection
    );
    statsCmd.Parameters.AddWithValue("userId", userId);
    statsCmd.Parameters.AddWithValue("deviceId", deviceId);
    statsCmd.Parameters.AddWithValue("weekStart", weekStart);
    statsCmd.Parameters.AddWithValue("weekEnd", weekEndExclusive);

    await using var statsReader = await statsCmd.ExecuteReaderAsync();
    await statsReader.ReadAsync();
    var karmaEarned = statsReader.GetInt32(0);
    var votesGiven = statsReader.GetInt32(1);
    var hakliGiven = statsReader.GetInt32(2);
    var haksizGiven = statsReader.GetInt32(3);
    var postsCreated = statsReader.GetInt32(4);
    var commentsPosted = statsReader.GetInt32(5);
    await statsReader.CloseAsync();

    await using var activityCmd = new NpgsqlCommand(
        """
        SELECT DISTINCT activity_day
        FROM (
            SELECT created_at::date AS activity_day FROM votes WHERE device_id = @deviceId AND created_at >= @streakWindow
            UNION
            SELECT created_at::date AS activity_day FROM posts WHERE user_id = @userId AND status != 'deleted' AND created_at >= @streakWindow
            UNION
            SELECT created_at::date AS activity_day FROM comments WHERE user_id = @userId AND status != 'deleted' AND created_at >= @streakWindow
        ) activity
        ORDER BY activity_day DESC
        """,
        connection
    );
    activityCmd.Parameters.AddWithValue("userId", userId);
    activityCmd.Parameters.AddWithValue("deviceId", deviceId);
    activityCmd.Parameters.AddWithValue("streakWindow", today.AddDays(-30));

    var activityDays = new HashSet<DateOnly>();
    await using var activityReader = await activityCmd.ExecuteReaderAsync();
    while (await activityReader.ReadAsync())
    {
        activityDays.Add(DateOnly.FromDateTime(activityReader.GetDateTime(0)));
    }

    var streak = 0;
    var cursor = DateOnly.FromDateTime(today);
    while (activityDays.Contains(cursor))
    {
        streak++;
        cursor = cursor.AddDays(-1);
    }

    return Results.Ok(new WeeklyStatsDto(
        weekLabel,
        karmaEarned,
        votesGiven,
        hakliGiven,
        haksizGiven,
        postsCreated,
        commentsPosted,
        streak
    ));
});

// â"€â"€ COMMENT PIN â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/posts/{id:guid}/comments/pin", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    if (!httpRequest.HasJsonContentType()) return Results.BadRequest();
    var body = await httpRequest.ReadFromJsonAsync<Dictionary<string, string>>();
    if (body is null || !body.TryGetValue("commentId", out var commentIdStr)
        || !Guid.TryParse(commentIdStr, out var commentId))
        return BadRequest("INVALID_BODY", "commentId gerekli.");

    await using var connection = await db.OpenConnectionAsync();

    // Post sahibi mi kontrol et
    await using var ownerCmd = new NpgsqlCommand(
        "SELECT 1 FROM posts WHERE id = @postId AND user_id = @userId AND status != 'deleted'",
        connection
    );
    ownerCmd.Parameters.AddWithValue("postId", id);
    ownerCmd.Parameters.AddWithValue("userId", userId);
    if (await ownerCmd.ExecuteScalarAsync() is null)
        return Forbid("NOT_POST_OWNER", "Sadece post sahibi yorum sabitleyebilir.");

    // Yorumun bu posta ait olduÄŸunu doÄŸrula
    await using var checkCmd = new NpgsqlCommand(
        "SELECT 1 FROM comments WHERE id = @commentId AND post_id = @postId AND status = 'active'",
        connection
    );
    checkCmd.Parameters.AddWithValue("commentId", commentId);
    checkCmd.Parameters.AddWithValue("postId", id);
    if (await checkCmd.ExecuteScalarAsync() is null)
        return NotFound("COMMENT_NOT_FOUND", "Yorum bulunamadÄ±.");

    await using var transaction = await connection.BeginTransactionAsync();
    // Mevcut sabiti kaldÄ±r
    await using var unpinCmd = new NpgsqlCommand(
        "UPDATE comments SET is_pinned = FALSE WHERE post_id = @postId AND is_pinned = TRUE",
        connection, transaction
    );
    unpinCmd.Parameters.AddWithValue("postId", id);
    await unpinCmd.ExecuteNonQueryAsync();

    // Yeni sabiti ekle
    await using var pinCmd = new NpgsqlCommand(
        "UPDATE comments SET is_pinned = TRUE WHERE id = @commentId",
        connection, transaction
    );
    pinCmd.Parameters.AddWithValue("commentId", commentId);
    await pinCmd.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
    return Results.NoContent();
});

app.MapDelete("/api/v1/posts/{id:guid}/comments/pin", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();

    await using var ownerCmd = new NpgsqlCommand(
        "SELECT 1 FROM posts WHERE id = @postId AND user_id = @userId AND status != 'deleted'",
        connection
    );
    ownerCmd.Parameters.AddWithValue("postId", id);
    ownerCmd.Parameters.AddWithValue("userId", userId);
    if (await ownerCmd.ExecuteScalarAsync() is null)
        return Forbid("NOT_POST_OWNER", "Sadece post sahibi sabiti kaldÄ±rabilir.");

    await using var cmd = new NpgsqlCommand(
        "UPDATE comments SET is_pinned = FALSE WHERE post_id = @postId AND is_pinned = TRUE",
        connection
    );
    cmd.Parameters.AddWithValue("postId", id);
    await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapGet("/api/v1/users/me/posts", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    int page = 1,
    int limit = 20,
    string sort = "new"
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
    var orderBy = sort switch
    {
        "old" => "p.created_at ASC",
        "votes" => "p.vote_count_hakli + p.vote_count_haksiz DESC, p.created_at DESC",
        "comments" => "p.comment_count DESC, p.created_at DESC",
        _ => "p.created_at DESC"
    };

    await using var connection = await db.OpenConnectionAsync();
    await using var countCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM posts WHERE user_id = @userId AND status != 'deleted'",
        connection
    );
    countCmd.Parameters.AddWithValue("userId", userId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        $"""
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               NULL::text, p.trend_score, p.created_at, TRUE,
               p.status, p.moderation_reason, p.is_anonymous
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        WHERE p.user_id = @userId AND p.status != 'deleted'
        ORDER BY {orderBy}
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var posts = await ReadPostsAsync(cmd);
    return Results.Ok(new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total)));
});

// â"€â"€ CHANGE PASSWORD â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapGet("/api/v1/users/me/saved", async (
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService,
    int page = 1,
    int limit = 20
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    await using var connection = await db.OpenConnectionAsync();
    var effectiveDeviceId = deviceId ?? await GetUserDeviceIdAsync(connection, userId);
    var deviceParam = effectiveDeviceId ?? Guid.Empty;

    await using var countCmd = new NpgsqlCommand(
        """
        SELECT COUNT(*)
        FROM saved_posts sp
        JOIN posts p ON p.id = sp.post_id
        WHERE sp.user_id = @userId AND p.status = 'active'
        """,
        connection
    );
    countCmd.Parameters.AddWithValue("userId", userId);
    var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

    await using var cmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags, p.content_source
        FROM saved_posts sp
        JOIN posts p ON p.id = sp.post_id
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE sp.user_id = @userId AND p.status = 'active'
        ORDER BY sp.created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.AddWithValue("deviceId", deviceParam);
    cmd.Parameters.AddWithValue("limit", limit);
    cmd.Parameters.AddWithValue("offset", offset);

    var posts = await ReadPostsAsync(cmd);
    return Results.Ok(new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total)));
});

app.MapPut("/api/v1/users/me/password", async (
    ChangePasswordRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError) return validationError;

    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();

    await using var selectCmd = new NpgsqlCommand(
        "SELECT password_hash, auth_provider FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    selectCmd.Parameters.AddWithValue("id", userId);
    await using var reader = await selectCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Unauthorized();

    var passwordHash = reader.IsDBNull(0) ? null : reader.GetString(0);
    var authProvider = reader.GetString(1);
    await reader.CloseAsync();

    if (authProvider == "google")
        return BadRequest("GOOGLE_ACCOUNT", "Google ile giriÅŸ yapan hesaplarda ÅŸifre deÄŸiÅŸtirilemez.");

    if (passwordHash is null || !PasswordService.Verify(request.CurrentPassword, passwordHash))
        return BadRequest("WRONG_PASSWORD", "Mevcut ÅŸifre hatalÄ±.");

    var newHash = PasswordService.Hash(request.NewPassword);
    await using var updateCmd = new NpgsqlCommand(
        "UPDATE users SET password_hash = @hash, updated_at = NOW() WHERE id = @id",
        connection
    );
    updateCmd.Parameters.AddWithValue("hash", newHash);
    updateCmd.Parameters.AddWithValue("id", userId);
    await updateCmd.ExecuteNonQueryAsync();

    return Results.NoContent();
});

// â"€â"€ FORGOT PASSWORD â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/auth/forgot-password", async (
    ForgotPasswordRequest request,
    Db db,
    EmailService emailService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError) return validationError;

    var email = request.Email.ToLowerInvariant();
    var dbKey = $"pwreset:{email}";

    await using var connection = await db.OpenConnectionAsync();

    await using var userCmd = new NpgsqlCommand(
        "SELECT 1 FROM users WHERE email = @email AND auth_provider = 'password' AND deleted_at IS NULL",
        connection
    );
    userCmd.Parameters.AddWithValue("email", email);
    if (await userCmd.ExecuteScalarAsync() is null)
        return Results.Ok(new MessageResponse("Eğer bu e-posta kayıtlıysa kod gönderildi."));

    // Redis: 3 dk cooldown, aynı pencerede aynı kod
    var (otp, tooSoon, waitSecs, isNewOtp) = await GetOrCreateCachedOtpAsync(
        redis.GetDb(), $"otp:pwreset:{email}",
        validFor: TimeSpan.FromMinutes(10),
        resendAfter: TimeSpan.FromMinutes(3));

    if (tooSoon)
        return TooManyRequests("OTP_TOO_SOON", $"Yeni kod için {waitSecs} saniye bekleyin.", waitSecs);

    // Yalnızca yeni OTP üretildiyse DB'yi güncelle; aynı kod resend ediliyorsa expires_at korunur
    if (isNewOtp)
    {
        await using var deleteCmd = new NpgsqlCommand("DELETE FROM email_otps WHERE email = @email", connection);
        deleteCmd.Parameters.AddWithValue("email", dbKey);
        await deleteCmd.ExecuteNonQueryAsync();

        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)", connection);
        insertCmd.Parameters.AddWithValue("email", dbKey);
        insertCmd.Parameters.AddWithValue("hash", PasswordService.HashOtp(otp!));
        await insertCmd.ExecuteNonQueryAsync();
    }

    _ = emailService.SendPasswordResetOtpAsync(email, otp!);
    return Results.Ok(new MessageResponse("Eğer bu e-posta kayıtlıysa kod gönderildi."));
}).RequireRateLimiting("auth-strict");

// â"€â"€ RESET PASSWORD â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/auth/reset-password", async (
    ResetPasswordRequest request,
    Db db,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } validationError) return validationError;

    var email = request.Email.ToLowerInvariant();
    var otpKey = $"pwreset:{email}";
    var otpHash = PasswordService.HashOtp(request.Otp);

    await using var connection = await db.OpenConnectionAsync();

    // OTP kontrol
    await using var otpCmd = new NpgsqlCommand(
        "SELECT id, otp_hash, attempts, expires_at FROM email_otps WHERE email = @email ORDER BY created_at DESC LIMIT 1",
        connection
    );
    otpCmd.Parameters.AddWithValue("email", otpKey);
    await using var otpReader = await otpCmd.ExecuteReaderAsync();
    if (!await otpReader.ReadAsync())
        return BadRequest("OTP_NOT_FOUND", "Aktif sÄ±fÄ±rlama kodu bulunamadÄ±.");

    var otpId = otpReader.GetGuid(0);
    var storedHash = otpReader.GetString(1);
    var attempts = otpReader.GetInt32(2);
    var expiresAt = otpReader.GetFieldValue<DateTimeOffset>(3);
    await otpReader.CloseAsync();

    if (expiresAt < DateTimeOffset.UtcNow)
        return BadRequest("OTP_EXPIRED", "SÄ±fÄ±rlama kodu sÃ¼resi dolmuÅŸ. Yeniden gÃ¶nder.");

    if (attempts >= 3)
        return BadRequest("OTP_MAX_ATTEMPTS", "Ã‡ok fazla hatalÄ± deneme. Yeni kod isteyin.");

    if (!string.Equals(storedHash, otpHash, StringComparison.OrdinalIgnoreCase))
    {
        await using var incCmd = new NpgsqlCommand(
            "UPDATE email_otps SET attempts = attempts + 1 WHERE id = @id",
            connection
        );
        incCmd.Parameters.AddWithValue("id", otpId);
        await incCmd.ExecuteNonQueryAsync();
        return BadRequest("OTP_INVALID", $"Kod hatalÄ±. {2 - attempts} deneme hakkÄ±n kaldÄ±.");
    }

    // OTP geÃ§erli â€" ÅŸifreyi gÃ¼ncelle ve OTP'yi sil
    var newHash = PasswordService.Hash(request.NewPassword);
    await using var transaction = await connection.BeginTransactionAsync();

    await using var updateCmd = new NpgsqlCommand(
        "UPDATE users SET password_hash = @hash, updated_at = NOW() WHERE email = @email AND auth_provider = 'password' AND deleted_at IS NULL",
        connection,
        transaction
    );
    updateCmd.Parameters.AddWithValue("hash", newHash);
    updateCmd.Parameters.AddWithValue("email", email);
    await updateCmd.ExecuteNonQueryAsync();

    await using var cleanupCmd = new NpgsqlCommand(
        "DELETE FROM email_otps WHERE email = @email",
        connection,
        transaction
    );
    cleanupCmd.Parameters.AddWithValue("email", otpKey);
    await cleanupCmd.ExecuteNonQueryAsync();

    await transaction.CommitAsync();
    await redis.GetDb().KeyDeleteAsync($"otp:pwreset:{email}");
    return Results.NoContent();
}).RequireRateLimiting("auth-strict");

app.MapGet("/api/v1/admin/analytics/cache", async (
    HttpRequest httpRequest,
    RedisService redis,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    var (hits, misses) = await redis.GetCacheMetricsAsync();
    var total = hits + misses;
    var hitRate = total == 0 ? 0 : Math.Round(hits * 100.0 / total, 1);

    return Results.Ok(new
    {
        hits,
        misses,
        total,
        hitRatePercent = hitRate,
        targetPercent = 85.0,
        isHealthy = total < 100 || hitRate >= 85.0
    });
});

app.MapGet("/api/v1/admin/analytics/overview", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT
            (SELECT COUNT(*) FROM devices WHERE last_seen_at >= NOW()::date) AS dau_today,
            (SELECT COUNT(*) FROM devices WHERE last_seen_at >= NOW()::date - INTERVAL '1 day' AND last_seen_at < NOW()::date) AS dau_yesterday,
            (SELECT COUNT(*) FROM posts WHERE created_at >= NOW()::date) AS posts_today,
            (SELECT COUNT(*) FROM reports WHERE status = 'pending') AS pending_reports,
            (
                SELECT COUNT(*)
                FROM (
                    SELECT id FROM posts WHERE status = 'under_review'
                    UNION ALL
                    SELECT id FROM comments WHERE status = 'under_review'
                ) queue
            ) AS under_review_posts,
            (SELECT COUNT(*) FROM posts WHERE status != 'deleted') AS total_posts,
            (SELECT COUNT(*) FROM comments WHERE status != 'deleted') AS total_comments,
            (SELECT COUNT(*) FROM votes) AS total_votes
        """,
        connection
    );

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new AdminOverviewResponse(
        new AdminOverviewStats(
            Convert.ToInt32(reader.GetInt64(0)),
            Convert.ToInt32(reader.GetInt64(1)),
            Convert.ToInt32(reader.GetInt64(2)),
            Convert.ToInt32(reader.GetInt64(3)),
            Convert.ToInt32(reader.GetInt64(4)),
            Convert.ToInt32(reader.GetInt64(5)),
            Convert.ToInt32(reader.GetInt64(6)),
            Convert.ToInt32(reader.GetInt64(7))
        )
    ));
});

app.MapGet("/api/v1/admin/analytics/velocity", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT
            (SELECT COUNT(*) FROM posts WHERE created_at >= NOW() - INTERVAL '1 hour') AS posts_last_hour,
            (SELECT COUNT(*) FROM votes WHERE created_at >= NOW() - INTERVAL '1 hour') AS votes_last_hour,
            (SELECT COALESCE(COUNT(*), 0) / NULLIF(EXTRACT(EPOCH FROM (NOW() - MIN(created_at))) / 3600.0, 0)
             FROM posts WHERE created_at >= NOW() - INTERVAL '7 days') AS avg_posts_per_hour_7d,
            (SELECT COALESCE(COUNT(*), 0) / NULLIF(EXTRACT(EPOCH FROM (NOW() - MIN(created_at))) / 3600.0, 0)
             FROM votes WHERE created_at >= NOW() - INTERVAL '7 days') AS avg_votes_per_hour_7d
        """,
        connection);

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();

    var postsLastHour = Convert.ToInt32(reader.GetInt64(0));
    var votesLastHour = Convert.ToInt32(reader.GetInt64(1));
    var avgPostsPerHour = reader.IsDBNull(2) ? 1.0 : Convert.ToDouble(reader.GetValue(2));
    var avgVotesPerHour = reader.IsDBNull(3) ? 1.0 : Convert.ToDouble(reader.GetValue(3));

    var postSpike = avgPostsPerHour > 0 && postsLastHour > avgPostsPerHour * 3;
    var voteSpike = avgVotesPerHour > 0 && votesLastHour > avgVotesPerHour * 3;

    return Results.Ok(new
    {
        postsLastHour,
        votesLastHour,
        avgPostsPerHour = Math.Round(avgPostsPerHour, 1),
        avgVotesPerHour = Math.Round(avgVotesPerHour, 1),
        postSpike,
        voteSpike,
        hasAnomaly = postSpike || voteSpike,
    });
});

app.MapGet("/api/v1/admin/analytics/trends", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    days = Math.Clamp(days, 1, 30);
    await using var connection = await db.OpenConnectionAsync();

    // GÃ¼nlÃ¼k post sayÄ±larÄ±
    await using var postCmd = new NpgsqlCommand(
        """
        SELECT series.day::date, COUNT(p.id)
        FROM (
            SELECT generate_series(NOW()::date - (@days - 1) * INTERVAL '1 day', NOW()::date, '1 day') AS day
        ) series
        LEFT JOIN posts p ON p.created_at::date = series.day
        GROUP BY series.day
        ORDER BY series.day ASC
        """,
        connection
    );
    postCmd.Parameters.AddWithValue("days", days);

    var postTrend = new List<object>();
    await using var postReader = await postCmd.ExecuteReaderAsync();
    while (await postReader.ReadAsync())
    {
        postTrend.Add(new { date = postReader.GetDateTime(0).ToString("yyyy-MM-dd"), count = postReader.GetInt64(1) });
    }
    await postReader.CloseAsync();

    // GÃ¼nlÃ¼k oy sayÄ±larÄ±
    await using var voteCmd = new NpgsqlCommand(
        """
        SELECT series.day::date, COUNT(v.post_id)
        FROM (
            SELECT generate_series(NOW()::date - (@days - 1) * INTERVAL '1 day', NOW()::date, '1 day') AS day
        ) series
        LEFT JOIN votes v ON v.created_at::date = series.day
        GROUP BY series.day
        ORDER BY series.day ASC
        """,
        connection
    );
    voteCmd.Parameters.AddWithValue("days", days);

    var voteTrend = new List<object>();
    await using var voteReader = await voteCmd.ExecuteReaderAsync();
    while (await voteReader.ReadAsync())
    {
        voteTrend.Add(new { date = voteReader.GetDateTime(0).ToString("yyyy-MM-dd"), count = voteReader.GetInt64(1) });
    }

    return Results.Ok(new { posts = postTrend, votes = voteTrend });
});

app.MapGet("/api/v1/admin/analytics/categories", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT c.name,
               COUNT(DISTINCT p.id) AS post_count,
               MAX(COALESCE(imp.impressions, 0)) AS impressions
        FROM categories c
        LEFT JOIN posts p ON p.category_id = c.id AND p.status != 'deleted'
        LEFT JOIN (
            SELECT dp.category_id, COUNT(*) AS impressions
            FROM discover_events de
            JOIN posts dp ON dp.id = de.post_id
            WHERE de.event_type = 'impression'
              AND de.created_at >= NOW() - (@days * INTERVAL '1 day')
            GROUP BY dp.category_id
        ) imp ON imp.category_id = c.id
        GROUP BY c.id, c.name
        ORDER BY post_count DESC
        """,
        connection
    );
    command.Parameters.AddWithValue("days", days);

    var data = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        data.Add(new
        {
            name       = reader.GetString(0),
            value      = Convert.ToInt64(reader.GetValue(1)),
            impressions = Convert.ToInt64(reader.GetValue(2)),
        });
    }

    return Results.Ok(data);
});

app.MapGet("/api/v1/admin/categories/health", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    days = Math.Clamp(days, 1, 30);
    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT
            c.id,
            c.name,
            c.emoji,
            COUNT(DISTINCT p.id) AS total_posts,
            AVG(CASE
                WHEN (p.vote_count_hakli + p.vote_count_haksiz) > 0
                 AND ABS(p.vote_count_hakli - p.vote_count_haksiz)::float
                     / (p.vote_count_hakli + p.vote_count_haksiz) < 0.2
                THEN 1.0 ELSE 0.0
            END) AS controversial_ratio,
            AVG(CASE
                WHEN EXTRACT(EPOCH FROM (NOW() - d.created_at)) / 86400.0 < 7
                THEN 1.0 ELSE 0.0
            END) AS new_user_comment_ratio,
            COUNT(DISTINCT r.id)::float / NULLIF(COUNT(DISTINCT v.id), 0) AS report_vote_ratio,
            AVG(p.perspective_toxicity) AS avg_toxicity
        FROM categories c
        LEFT JOIN posts p ON p.category_id = c.id
            AND p.status = 'active'
            AND p.created_at > NOW() - (@days || ' days')::INTERVAL
        LEFT JOIN comments cm ON cm.post_id = p.id AND cm.status = 'active'
        LEFT JOIN devices d ON d.id = cm.device_id
        LEFT JOIN reports r ON r.post_id = p.id
        LEFT JOIN votes v ON v.post_id = p.id
        WHERE c.id > 0
        GROUP BY c.id, c.name, c.emoji
        ORDER BY COALESCE(controversial_ratio, 0) DESC
        """,
        connection
    );
    cmd.Parameters.AddWithValue("days", days);

    var items = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            emoji = reader.GetString(2),
            totalPosts = Convert.ToInt64(reader.GetValue(3)),
            controversialRatio = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
            newUserCommentRatio = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
            reportVoteRatio = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6),
            avgToxicity = reader.IsDBNull(7) ? null : (double?)reader.GetDouble(7),
        });
    }

    return Results.Ok(new { days, categories = items });
});

app.MapGet("/api/v1/admin/analytics/moderation", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
    {
        return unauthorized;
    }

    days = Math.Clamp(days, 1, 30);
    await using var connection = await db.OpenConnectionAsync();

    await using var summaryCmd = new NpgsqlCommand(
        """
        SELECT
            (SELECT COUNT(*) FROM reports WHERE created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day') AS total_reports,
            (SELECT COUNT(*) FROM reports WHERE status = 'pending') AS pending_reports,
            (SELECT COUNT(*) FROM reports WHERE status = 'actioned' AND created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day') AS actioned_reports,
            (SELECT COUNT(*) FROM reports WHERE status = 'dismissed' AND created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day') AS dismissed_reports,
            (
                SELECT COUNT(*)
                FROM (
                    SELECT target_type, target_id
                    FROM reports
                    WHERE created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day'
                    GROUP BY target_type, target_id
                    HAVING COUNT(*) > 1
                ) repeated
            ) AS repeated_targets,
            (
                SELECT COALESCE(AVG(EXTRACT(EPOCH FROM (aa.created_at - r.created_at)) / 3600), 0)
                FROM reports r
                JOIN admin_actions aa
                  ON aa.target_type = r.target_type
                 AND aa.target_id = r.target_id
                 AND aa.action LIKE 'report_%'
                 AND aa.created_at >= r.created_at
                WHERE r.status IN ('actioned', 'dismissed')
                  AND r.created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day'
            ) AS avg_review_hours
        """,
        connection);
    summaryCmd.Parameters.AddWithValue("days", days);

    await using var summaryReader = await summaryCmd.ExecuteReaderAsync();
    await summaryReader.ReadAsync();
    var totalReports = Convert.ToInt32(summaryReader.GetInt64(0));
    var pendingReports = Convert.ToInt32(summaryReader.GetInt64(1));
    var actionedReports = Convert.ToInt32(summaryReader.GetInt64(2));
    var dismissedReports = Convert.ToInt32(summaryReader.GetInt64(3));
    var repeatedTargets = Convert.ToInt32(summaryReader.GetInt64(4));
    var avgReviewHours = Math.Round(Convert.ToDouble(summaryReader.GetValue(5)), 1);
    await summaryReader.CloseAsync();

    var resolvedReports = actionedReports + dismissedReports;
    var actionRate = resolvedReports == 0 ? 0 : Math.Round(actionedReports * 100.0 / resolvedReports, 1);
    var falsePositiveRate = resolvedReports == 0 ? 0 : Math.Round(dismissedReports * 100.0 / resolvedReports, 1);

    await using var categoryCmd = new NpgsqlCommand(
        """
        SELECT reason, COUNT(*)
        FROM reports
        WHERE created_at >= NOW()::date - (@days - 1) * INTERVAL '1 day'
        GROUP BY reason
        ORDER BY COUNT(*) DESC
        """,
        connection);
    categoryCmd.Parameters.AddWithValue("days", days);

    var categories = new List<object>();
    await using var categoryReader = await categoryCmd.ExecuteReaderAsync();
    while (await categoryReader.ReadAsync())
    {
        categories.Add(new
        {
            reason = categoryReader.GetString(0),
            count = Convert.ToInt32(categoryReader.GetInt64(1)),
        });
    }
    await categoryReader.CloseAsync();

    await using var dailyCmd = new NpgsqlCommand(
        """
        SELECT series.day::date, COUNT(r.id)
        FROM (
            SELECT generate_series(NOW()::date - (@days - 1) * INTERVAL '1 day', NOW()::date, '1 day') AS day
        ) series
        LEFT JOIN reports r ON r.created_at::date = series.day
        GROUP BY series.day
        ORDER BY series.day ASC
        """,
        connection);
    dailyCmd.Parameters.AddWithValue("days", days);

    var daily = new List<object>();
    await using var dailyReader = await dailyCmd.ExecuteReaderAsync();
    while (await dailyReader.ReadAsync())
    {
        daily.Add(new
        {
            date = dailyReader.GetDateTime(0).ToString("yyyy-MM-dd"),
            count = Convert.ToInt32(dailyReader.GetInt64(1)),
        });
    }
    await dailyReader.CloseAsync();

    // Appeal overturn rate
    await using var appealCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE status = 'pending') AS pending,
            COUNT(*) FILTER (WHERE status = 'approved') AS approved,
            COUNT(*) FILTER (WHERE status = 'rejected') AS rejected
        FROM moderation_appeals
        WHERE created_at >= NOW() - @days * INTERVAL '1 day'
        """,
        connection);
    appealCmd.Parameters.AddWithValue("days", days);
    await using var appealReader = await appealCmd.ExecuteReaderAsync();
    await appealReader.ReadAsync();
    var appealsPending = Convert.ToInt32(appealReader.GetInt64(0));
    var appealsApproved = Convert.ToInt32(appealReader.GetInt64(1));
    var appealsRejected = Convert.ToInt32(appealReader.GetInt64(2));
    await appealReader.CloseAsync();
    var appealsTotal = appealsApproved + appealsRejected;
    var appealOverturnRate = appealsTotal == 0 ? 0.0 : Math.Round(appealsApproved * 100.0 / appealsTotal, 1);

    return Results.Ok(new
    {
        totalReports,
        pendingReports,
        actionedReports,
        dismissedReports,
        avgReviewHours,
        actionRate,
        falsePositiveRate,
        repeatedTargets,
        categories,
        daily,
        appealsPending,
        appealsApproved,
        appealsRejected,
        appealOverturnRate,
    });
});

// ── MODERATOR PERFORMANCE ANALYTICS ─────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/moderators", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        SELECT
            admin_email,
            COUNT(*) AS total_actions,
            COUNT(*) FILTER (WHERE action IN ('post_approved', 'comment_approved')) AS approvals,
            COUNT(*) FILTER (WHERE action IN ('post_rejected', 'post_deleted', 'comment_rejected', 'comment_deleted')) AS removals,
            COUNT(*) FILTER (WHERE action IN ('user_banned', 'user_warned')) AS user_sanctions,
            MIN(created_at) AS first_action,
            MAX(created_at) AS last_action
        FROM admin_actions
        WHERE created_at >= NOW() - @days * INTERVAL '1 day'
        GROUP BY admin_email
        ORDER BY total_actions DESC
        """,
        connection);
    cmd.Parameters.AddWithValue("days", days);

    var moderators = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        moderators.Add(new
        {
            adminEmail = reader.GetString(0),
            totalActions = Convert.ToInt32(reader.GetInt64(1)),
            approvals = Convert.ToInt32(reader.GetInt64(2)),
            removals = Convert.ToInt32(reader.GetInt64(3)),
            userSanctions = Convert.ToInt32(reader.GetInt64(4)),
            firstAction = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            lastAction = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
        });
    }

    return Results.Ok(new { moderators, days });
});

// ── RETENTION ANALYTICS ────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/retention", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int cohortDays = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    cohortDays = Math.Clamp(cohortDays, 7, 90);
    await using var connection = await db.OpenConnectionAsync();

    // D1/D7/D30 retention: % of devices created N+ days ago that returned within N days of registration
    await using var retentionCmd = new NpgsqlCommand(
        """
        SELECT
            ROUND(
                100.0 * COUNT(*) FILTER (WHERE last_seen_at > created_at + INTERVAL '1 day')
                / NULLIF(COUNT(*) FILTER (WHERE created_at <= NOW() - INTERVAL '1 day'), 0)
            , 1) AS d1_retention,
            ROUND(
                100.0 * COUNT(*) FILTER (WHERE last_seen_at > created_at + INTERVAL '7 days')
                / NULLIF(COUNT(*) FILTER (WHERE created_at <= NOW() - INTERVAL '7 days'), 0)
            , 1) AS d7_retention,
            ROUND(
                100.0 * COUNT(*) FILTER (WHERE last_seen_at > created_at + INTERVAL '30 days')
                / NULLIF(COUNT(*) FILTER (WHERE created_at <= NOW() - INTERVAL '30 days'), 0)
            , 1) AS d30_retention,
            COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '1 day') AS new_devices_today,
            COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '7 days') AS new_devices_7d,
            COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '30 days') AS new_devices_30d
        FROM devices
        WHERE created_at >= NOW() - (@cohortDays * INTERVAL '1 day')
        """,
        connection);
    retentionCmd.Parameters.AddWithValue("cohortDays", cohortDays);

    await using var retentionReader = await retentionCmd.ExecuteReaderAsync();
    await retentionReader.ReadAsync();
    var d1 = retentionReader.IsDBNull(0) ? (double?)null : Convert.ToDouble(retentionReader.GetValue(0));
    var d7 = retentionReader.IsDBNull(1) ? (double?)null : Convert.ToDouble(retentionReader.GetValue(1));
    var d30 = retentionReader.IsDBNull(2) ? (double?)null : Convert.ToDouble(retentionReader.GetValue(2));
    var newToday = Convert.ToInt64(retentionReader.GetValue(3));
    var new7d = Convert.ToInt64(retentionReader.GetValue(4));
    var new30d = Convert.ToInt64(retentionReader.GetValue(5));
    await retentionReader.CloseAsync();

    // Daily cohort breakdown: registration date → D1 retained count
    await using var cohortCmd = new NpgsqlCommand(
        """
        SELECT
            created_at::date AS cohort_date,
            COUNT(*) AS registered,
            COUNT(*) FILTER (WHERE last_seen_at > created_at + INTERVAL '1 day') AS d1_retained,
            COUNT(*) FILTER (WHERE last_seen_at > created_at + INTERVAL '7 days') AS d7_retained
        FROM devices
        WHERE created_at >= NOW() - (@cohortDays * INTERVAL '1 day')
        GROUP BY cohort_date
        ORDER BY cohort_date ASC
        """,
        connection);
    cohortCmd.Parameters.AddWithValue("cohortDays", cohortDays);

    var cohorts = new List<object>();
    await using var cohortReader = await cohortCmd.ExecuteReaderAsync();
    while (await cohortReader.ReadAsync())
    {
        var registered = Convert.ToInt64(cohortReader.GetValue(1));
        var d1Retained = Convert.ToInt64(cohortReader.GetValue(2));
        var d7Retained = Convert.ToInt64(cohortReader.GetValue(3));
        cohorts.Add(new
        {
            date = cohortReader.GetDateTime(0).ToString("yyyy-MM-dd"),
            registered,
            d1Retained,
            d7Retained,
            d1Rate = registered == 0 ? 0 : Math.Round(d1Retained * 100.0 / registered, 1),
            d7Rate = registered == 0 ? 0 : Math.Round(d7Retained * 100.0 / registered, 1),
        });
    }

    return Results.Ok(new
    {
        d1RetentionPercent = d1,
        d7RetentionPercent = d7,
        d30RetentionPercent = d30,
        newDevicesToday = newToday,
        newDevices7d = new7d,
        newDevices30d = new30d,
        cohorts,
        targets = new { d1 = 30.0, d7 = 15.0 },
    });
});

// ── NOTIFICATION ANALYTICS ──────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/notifications", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var funnelCmd = new NpgsqlCommand(
        """
        SELECT event_type, COUNT(*) AS cnt
        FROM notification_events
        WHERE occurred_at >= NOW() - (@days * INTERVAL '1 day')
        GROUP BY event_type
        """,
        connection);
    funnelCmd.Parameters.AddWithValue("days", days);

    var funnel = new Dictionary<string, long>();
    await using var funnelReader = await funnelCmd.ExecuteReaderAsync();
    while (await funnelReader.ReadAsync())
        funnel[funnelReader.GetString(0)] = Convert.ToInt64(funnelReader.GetValue(1));
    await funnelReader.CloseAsync();

    await using var suppressCmd = new NpgsqlCommand(
        """
        SELECT COALESCE(metadata->>'reason', 'unknown') AS reason, COUNT(*) AS cnt
        FROM notification_events
        WHERE event_type = 'suppressed'
          AND occurred_at >= NOW() - (@days * INTERVAL '1 day')
        GROUP BY reason
        ORDER BY cnt DESC
        """,
        connection);
    suppressCmd.Parameters.AddWithValue("days", days);

    var suppressionReasons = new List<object>();
    await using var suppressReader = await suppressCmd.ExecuteReaderAsync();
    while (await suppressReader.ReadAsync())
        suppressionReasons.Add(new { reason = suppressReader.GetString(0), count = Convert.ToInt64(suppressReader.GetValue(1)) });
    await suppressReader.CloseAsync();

    await using var tokensCmd = new NpgsqlCommand("SELECT COUNT(*) FROM fcm_tokens", connection);
    var activeTokens = Convert.ToInt64(await tokensCmd.ExecuteScalarAsync());

    await using var newTokensCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM fcm_tokens WHERE created_at >= NOW() - (@days * INTERVAL '1 day')",
        connection);
    newTokensCmd.Parameters.AddWithValue("days", days);
    var newTokens = Convert.ToInt64(await newTokensCmd.ExecuteScalarAsync());

    await using var viralCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(ne.id) FILTER (WHERE ne.event_type = 'sent')   AS viral_sent,
            COUNT(ne.id) FILTER (WHERE ne.event_type = 'opened') AS viral_opened
        FROM notification_events ne
        JOIN notifications n ON n.id = ne.notification_id
        WHERE n.type IN ('viral_post_owner', 'trend_alert')
          AND ne.occurred_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    viralCmd.Parameters.AddWithValue("days", days);

    await using var viralReader = await viralCmd.ExecuteReaderAsync();
    long viralSent = 0, viralOpened = 0;
    if (await viralReader.ReadAsync())
    {
        viralSent = viralReader.IsDBNull(0) ? 0 : Convert.ToInt64(viralReader.GetValue(0));
        viralOpened = viralReader.IsDBNull(1) ? 0 : Convert.ToInt64(viralReader.GetValue(1));
    }
    await viralReader.CloseAsync();

    long Get(string key) => funnel.TryGetValue(key, out var v) ? v : 0;
    var sent = Get("sent");
    var sendAttempt = Get("send_attempt");
    var opened = Get("opened");

    return Results.Ok(new
    {
        days,
        funnel = new
        {
            intent    = Get("intent"),
            eligible  = Get("eligible"),
            suppressed = Get("suppressed"),
            sendAttempt,
            sent,
            failed    = Get("failed"),
            retrying  = Get("retrying"),
            opened,
            dismissed = Get("dismissed"),
            read      = Get("read"),
        },
        suppressionReasons,
        activeTokens,
        newTokensPeriod = newTokens,
        viralSent,
        viralOpened,
        deliveryRate         = sendAttempt == 0 ? 0.0 : Math.Round(sent * 100.0 / sendAttempt, 1),
        openRate             = sent == 0 ? 0.0 : Math.Round(opened * 100.0 / sent, 1),
        viralConversionRate  = viralSent == 0 ? 0.0 : Math.Round(viralOpened * 100.0 / viralSent, 1),
    });
});

// ── FEED QUALITY ─────────────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/feed-quality", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var totalsCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE event_type = 'impression')     AS impressions,
            COUNT(*) FILTER (WHERE event_type = 'dwell')          AS dwell,
            COUNT(*) FILTER (WHERE event_type = 'skip')           AS skip,
            COUNT(*) FILTER (WHERE event_type = 'vote')           AS votes,
            COUNT(*) FILTER (WHERE event_type = 'comment_open')   AS comment_opens,
            COUNT(*) FILTER (WHERE event_type = 'share')          AS shares,
            COUNT(*) FILTER (WHERE event_type = 'not_interested') AS not_interested,
            COALESCE(AVG(dwell_seconds) FILTER (WHERE event_type = 'dwell'), 0) AS avg_dwell_seconds
        FROM discover_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    totalsCmd.Parameters.AddWithValue("days", days);

    long impressions = 0, dwell = 0, skip = 0, votes = 0, commentOpens = 0, shares = 0, notInterested = 0;
    double avgDwellSeconds = 0.0;

    await using var totalsReader = await totalsCmd.ExecuteReaderAsync();
    if (await totalsReader.ReadAsync())
    {
        impressions     = totalsReader.IsDBNull(0) ? 0 : Convert.ToInt64(totalsReader.GetValue(0));
        dwell           = totalsReader.IsDBNull(1) ? 0 : Convert.ToInt64(totalsReader.GetValue(1));
        skip            = totalsReader.IsDBNull(2) ? 0 : Convert.ToInt64(totalsReader.GetValue(2));
        votes           = totalsReader.IsDBNull(3) ? 0 : Convert.ToInt64(totalsReader.GetValue(3));
        commentOpens    = totalsReader.IsDBNull(4) ? 0 : Convert.ToInt64(totalsReader.GetValue(4));
        shares          = totalsReader.IsDBNull(5) ? 0 : Convert.ToInt64(totalsReader.GetValue(5));
        notInterested   = totalsReader.IsDBNull(6) ? 0 : Convert.ToInt64(totalsReader.GetValue(6));
        avgDwellSeconds = totalsReader.IsDBNull(7) ? 0.0 : Convert.ToDouble(totalsReader.GetValue(7));
    }
    await totalsReader.CloseAsync();

    await using var breakdownCmd = new NpgsqlCommand(
        """
        SELECT
            COALESCE(metadata->>'ranking_reason', 'unknown') AS ranking_reason,
            COUNT(*) FILTER (WHERE event_type = 'impression')     AS impressions,
            COUNT(*) FILTER (WHERE event_type = 'vote')           AS votes,
            COUNT(*) FILTER (WHERE event_type = 'skip')           AS skip,
            COUNT(*) FILTER (WHERE event_type = 'not_interested') AS not_interested
        FROM discover_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        GROUP BY metadata->>'ranking_reason'
        ORDER BY COUNT(*) FILTER (WHERE event_type = 'impression') DESC
        LIMIT 20
        """,
        connection);
    breakdownCmd.Parameters.AddWithValue("days", days);

    const long MinImpressions = 100;

    var byRankingReason = new List<object>();
    await using var breakdownReader = await breakdownCmd.ExecuteReaderAsync();
    while (await breakdownReader.ReadAsync())
    {
        var rr           = breakdownReader.GetString(0);
        var rrImpr       = breakdownReader.IsDBNull(1) ? 0 : Convert.ToInt64(breakdownReader.GetValue(1));
        var rrVotes      = breakdownReader.IsDBNull(2) ? 0 : Convert.ToInt64(breakdownReader.GetValue(2));
        var rrSkip       = breakdownReader.IsDBNull(3) ? 0 : Convert.ToInt64(breakdownReader.GetValue(3));
        var rrNotInt     = breakdownReader.IsDBNull(4) ? 0 : Convert.ToInt64(breakdownReader.GetValue(4));
        var rrVoteRate   = FqRate(rrVotes, rrImpr);
        var rrSkipRate   = FqRate(rrSkip, rrImpr);
        var rrNotIntRate = FqRate(rrNotInt, rrImpr);
        var rrHealth     = rrImpr >= MinImpressions && (rrSkipRate > 55 || rrNotIntRate > 10)
            ? "critical"
            : rrImpr >= MinImpressions && rrVoteRate < 8
                ? "warning"
                : "healthy";
        byRankingReason.Add(new
        {
            rankingReason     = rr,
            impressions       = rrImpr,
            votes             = rrVotes,
            skip              = rrSkip,
            notInterested     = rrNotInt,
            voteRate          = rrVoteRate,
            skipRate          = rrSkipRate,
            notInterestedRate = rrNotIntRate,
            healthStatus      = rrHealth,
        });
    }
    await breakdownReader.CloseAsync();

    static double FqRate(long n, long d) => d == 0 ? 0.0 : Math.Round(n * 100.0 / d, 1);

    var dwellRate         = FqRate(dwell, impressions);
    var voteRate          = FqRate(votes, impressions);
    var skipRate          = FqRate(skip, impressions);
    var notInterestedRate = FqRate(notInterested, impressions);
    var shareRate         = FqRate(shares, impressions);
    var commentOpenRate   = FqRate(commentOpens, impressions);
    var healthReasons = new List<string>();

    if (impressions >= MinImpressions && skipRate > 55)
        healthReasons.Add($"Skip orani cok yuksek: %{skipRate} (esik: >55)");
    if (impressions >= MinImpressions && notInterestedRate > 10)
        healthReasons.Add($"Ilgilenmiyor orani cok yuksek: %{notInterestedRate} (esik: >10)");
    if (impressions >= MinImpressions && voteRate < 8)
        healthReasons.Add($"Oy orani cok dusuk: %{voteRate} (esik: <8)");
    if (impressions >= MinImpressions && dwellRate < 25)
        healthReasons.Add($"Dwell orani cok dusuk: %{dwellRate} (esik: <25)");

    var healthStatus = healthReasons.Any(r => r.Contains("Skip") || r.Contains("Ilgilenmiyor"))
        ? "critical"
        : healthReasons.Count > 0
            ? "warning"
            : "healthy";

    return Results.Ok(new
    {
        days,
        totals = new
        {
            impressions,
            dwell,
            skip,
            votes,
            commentOpens,
            shares,
            notInterested,
            avgDwellSeconds = Math.Round(avgDwellSeconds, 1),
        },
        rates = new
        {
            dwellRate,
            voteRate,
            skipRate,
            notInterestedRate,
            shareRate,
            commentOpenRate,
        },
        byRankingReason,
        health = new
        {
            status  = healthStatus,
            reasons = healthReasons,
        },
    });
});

// ── FEED QUALITY DRILL-DOWN ───────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/feed-quality/posts", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7,
    string? rankingReason = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        WITH per_post AS (
            SELECT
                post_id,
                COALESCE(metadata->>'ranking_reason', 'unknown')      AS ranking_reason,
                COUNT(*) FILTER (WHERE event_type = 'impression')     AS impressions,
                COUNT(*) FILTER (WHERE event_type = 'skip')           AS skip,
                COUNT(*) FILTER (WHERE event_type = 'not_interested') AS not_interested,
                COUNT(*) FILTER (WHERE event_type = 'vote')           AS votes
            FROM discover_events
            WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
              AND (@rankingReason IS NULL
                   OR metadata->>'ranking_reason' = @rankingReason)
            GROUP BY post_id, metadata->>'ranking_reason'
        )
        SELECT
            pp.post_id::text,
            pp.ranking_reason,
            pp.impressions,
            pp.skip,
            pp.not_interested,
            pp.votes,
            p.title,
            c.name         AS category_name,
            p.status,
            p.created_at,
            p.trend_score,
            COUNT(r.id)    AS report_count
        FROM per_post pp
        JOIN posts      p ON p.id = pp.post_id
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN reports r ON r.target_type = 'post' AND r.target_id = pp.post_id
        WHERE pp.impressions >= 5
          AND (
              skip * 1.0 / NULLIF(impressions, 0) >= 0.45
              OR not_interested * 1.0 / NULLIF(impressions, 0) >= 0.08
              OR votes * 1.0 / NULLIF(impressions, 0) < 0.08
          )
        GROUP BY
            pp.post_id, pp.ranking_reason, pp.impressions, pp.skip,
            pp.not_interested, pp.votes, p.title, c.name, p.status,
            p.created_at, p.trend_score
        ORDER BY
            (
                (skip * 1.0 / NULLIF(impressions, 0)) * 2.0 +
                (not_interested * 1.0 / NULLIF(impressions, 0)) * 3.0 +
                CASE WHEN votes * 1.0 / NULLIF(impressions, 0) < 0.08 THEN 1.0 ELSE 0.0 END
            ) DESC,
            pp.impressions DESC
        LIMIT 50
        """,
        connection);
    cmd.Parameters.AddWithValue("days", days);
    cmd.Parameters.AddWithValue("rankingReason", rankingReason as object ?? DBNull.Value);

    var posts = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var postId       = reader.GetString(0);
        var rr           = reader.GetString(1);
        var impr         = Convert.ToInt64(reader.GetValue(2));
        var sk           = Convert.ToInt64(reader.GetValue(3));
        var notInt       = Convert.ToInt64(reader.GetValue(4));
        var vt           = Convert.ToInt64(reader.GetValue(5));
        var title        = reader.GetString(6);
        var categoryName = reader.GetString(7);
        var status       = reader.GetString(8);
        var createdAt    = reader.GetFieldValue<DateTimeOffset>(9);
        var trendScore   = reader.IsDBNull(10) ? 0.0 : Convert.ToDouble(reader.GetValue(10));
        var reportCount  = Convert.ToInt64(reader.GetValue(11));

        static double Dr(long n, long d) => d == 0 ? 0.0 : Math.Round(n * 100.0 / d, 1);
        posts.Add(new
        {
            postId,
            rankingReason     = rr,
            impressions       = impr,
            skip              = sk,
            notInterested     = notInt,
            votes             = vt,
            skipRate          = Dr(sk, impr),
            notInterestedRate = Dr(notInt, impr),
            voteRate          = Dr(vt, impr),
            title,
            categoryName,
            status,
            createdAt,
            trendScore        = Math.Round(trendScore, 2),
            reportCount,
        });
    }

    return Results.Ok(new { days, rankingReason, posts });
});

// ── FEED QUALITY TIMESERIES ──────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/feed-quality/timeseries", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 30
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var cmd = new NpgsqlCommand(
        """
        WITH day_series AS (
            SELECT generate_series(
                (NOW()::date - ((@days - 1) * INTERVAL '1 day'))::date,
                NOW()::date,
                '1 day'::interval
            )::date AS day
        ),
        daily AS (
            SELECT
                created_at::date                                                        AS day,
                COUNT(*) FILTER (WHERE event_type = 'impression')                       AS impressions,
                COUNT(*) FILTER (WHERE event_type = 'dwell')                            AS dwell,
                COUNT(*) FILTER (WHERE event_type = 'skip')                             AS skip,
                COUNT(*) FILTER (WHERE event_type = 'vote')                             AS votes,
                COUNT(*) FILTER (WHERE event_type = 'not_interested')                   AS not_interested
            FROM discover_events
            WHERE created_at >= (NOW()::date - ((@days - 1) * INTERVAL '1 day'))
            GROUP BY created_at::date
        )
        SELECT
            ds.day::text,
            COALESCE(d.impressions,    0) AS impressions,
            COALESCE(d.dwell,          0) AS dwell,
            COALESCE(d.skip,           0) AS skip,
            COALESCE(d.votes,          0) AS votes,
            COALESCE(d.not_interested, 0) AS not_interested
        FROM day_series ds
        LEFT JOIN daily d ON d.day = ds.day
        ORDER BY ds.day
        """,
        connection);
    cmd.Parameters.AddWithValue("days", days);

    const long MinImpressions = 100;
    static double Rt(long n, long d) => d == 0 ? 0.0 : Math.Round(n * 100.0 / d, 1);
    string TsDayHealth(long impr, double skipRate, double notIntRate, double voteRate, double dwellRate)
    {
        if (impr >= MinImpressions && (skipRate > 55 || notIntRate > 10)) return "critical";
        if (impr >= MinImpressions && (voteRate < 8 || dwellRate < 25)) return "warning";
        return "healthy";
    }

    var series = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var date   = reader.GetString(0);
        var impr   = Convert.ToInt64(reader.GetValue(1));
        var dw     = Convert.ToInt64(reader.GetValue(2));
        var sk     = Convert.ToInt64(reader.GetValue(3));
        var vt     = Convert.ToInt64(reader.GetValue(4));
        var notInt = Convert.ToInt64(reader.GetValue(5));

        var dwellRate         = Rt(dw, impr);
        var voteRate          = Rt(vt, impr);
        var skipRate          = Rt(sk, impr);
        var notInterestedRate = Rt(notInt, impr);
        var healthStatus      = TsDayHealth(impr, skipRate, notInterestedRate, voteRate, dwellRate);

        series.Add(new
        {
            date,
            impressions       = impr,
            dwellRate,
            voteRate,
            skipRate,
            notInterestedRate,
            healthStatus,
        });
    }

    return Results.Ok(new { days, series });
});

// ── FEED QUALITY EXPORT ──────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/feed-quality/export", async (
    HttpRequest httpRequest,
    HttpResponse httpResponse,
    Db db,
    AdminAuthService adminAuth,
    int days = 30,
    string format = "csv"
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;
    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

    days = Math.Clamp(days, 1, 90);
    if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        return BadRequest("INVALID_FORMAT", "Sadece csv formatı desteklenir.");

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        WITH day_series AS (
            SELECT generate_series(
                (NOW()::date - ((@days - 1) * INTERVAL '1 day'))::date,
                NOW()::date,
                '1 day'::interval
            )::date AS day
        ),
        daily AS (
            SELECT
                created_at::date                                      AS day,
                COUNT(*) FILTER (WHERE event_type = 'impression')     AS impressions,
                COUNT(*) FILTER (WHERE event_type = 'dwell')          AS dwell,
                COUNT(*) FILTER (WHERE event_type = 'skip')           AS skip,
                COUNT(*) FILTER (WHERE event_type = 'vote')           AS votes,
                COUNT(*) FILTER (WHERE event_type = 'not_interested') AS not_interested
            FROM discover_events
            WHERE created_at >= (NOW()::date - ((@days - 1) * INTERVAL '1 day'))
            GROUP BY created_at::date
        )
        SELECT
            ds.day::text,
            COALESCE(d.impressions,    0) AS impressions,
            COALESCE(d.dwell,          0) AS dwell,
            COALESCE(d.skip,           0) AS skip,
            COALESCE(d.votes,          0) AS votes,
            COALESCE(d.not_interested, 0) AS not_interested
        FROM day_series ds
        LEFT JOIN daily d ON d.day = ds.day
        ORDER BY ds.day
        """,
        connection);
    cmd.Parameters.AddWithValue("days", days);

    const long MinImpressions = 100;
    static double CsvRate(long n, long d) => d == 0 ? 0.0 : Math.Round(n * 100.0 / d, 1);
    static string CsvHealth(long impr, double skipRate, double notIntRate, double voteRate, double dwellRate)
    {
        if (impr >= MinImpressions && (skipRate > 55 || notIntRate > 10)) return "critical";
        if (impr >= MinImpressions && (voteRate < 8 || dwellRate < 25)) return "warning";
        return "healthy";
    }

    var csv = new StringBuilder();
    csv.AppendLine("date,impressions,dwell_rate,vote_rate,skip_rate,not_interested_rate,health_status");

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var date   = reader.GetString(0);
        var impr   = Convert.ToInt64(reader.GetValue(1));
        var dwell  = Convert.ToInt64(reader.GetValue(2));
        var skip   = Convert.ToInt64(reader.GetValue(3));
        var votes  = Convert.ToInt64(reader.GetValue(4));
        var notInt = Convert.ToInt64(reader.GetValue(5));

        var dwellRate         = CsvRate(dwell, impr);
        var voteRate          = CsvRate(votes, impr);
        var skipRate          = CsvRate(skip, impr);
        var notInterestedRate = CsvRate(notInt, impr);
        var healthStatus      = CsvHealth(impr, skipRate, notInterestedRate, voteRate, dwellRate);

        csv.AppendLine(string.Join(",", [
            date,
            impr.ToString(CultureInfo.InvariantCulture),
            dwellRate.ToString(CultureInfo.InvariantCulture),
            voteRate.ToString(CultureInfo.InvariantCulture),
            skipRate.ToString(CultureInfo.InvariantCulture),
            notInterestedRate.ToString(CultureInfo.InvariantCulture),
            healthStatus,
        ]));
    }
    await reader.CloseAsync();

    await using var auditTx = await connection.BeginTransactionAsync();
    await LogAdminActionAsync(
        connection,
        auditTx,
        adminEmail,
        "analytics_exported",
        "analytics",
        null,
        $"report=feed_quality, days={days}, format=csv");
    await auditTx.CommitAsync();

    httpResponse.Headers["Content-Disposition"] =
        $"attachment; filename=\"feed-quality-{days}d-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv\"";
    return Results.Text(csv.ToString(), "text/csv; charset=utf-8", Encoding.UTF8);
}).RequireRateLimiting("admin-analytics-export");

// -- REPORT CENTER ANALYTICS --------------------------------------------------

app.MapGet("/api/v1/admin/analytics/activity", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    DateTimeOffset? from = null,
    DateTimeOffset? to = null,
    string groupBy = "day",
    string? platform = null,
    string? userType = null,
    string? source = null,
    int? categoryId = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var range = NormalizeAnalyticsRange(from, to, groupBy);
    if (range is null)
        return BadRequest("INVALID_RANGE", "Tarih araligi veya groupBy gecersiz.");

    await using var connection = await db.OpenConnectionAsync();

    async Task<object> ReadSummaryAsync(DateTimeOffset start, DateTimeOffset end)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                (SELECT COUNT(DISTINCT d.id)
                 FROM devices d
                 WHERE d.last_seen_at >= @from AND d.last_seen_at < @to
                   AND (@platform IS NULL OR d.platform = @platform)
                   AND (@userType IS NULL
                        OR (@userType = 'registered' AND EXISTS (SELECT 1 FROM users u WHERE u.device_id = d.id AND u.deleted_at IS NULL))
                        OR (@userType = 'guest' AND NOT EXISTS (SELECT 1 FROM users u WHERE u.device_id = d.id AND u.deleted_at IS NULL)))) AS active_devices,
                (SELECT COUNT(*)
                 FROM posts p
                 JOIN devices pd ON pd.id = p.device_id
                 WHERE p.created_at >= @from AND p.created_at < @to
                   AND p.status <> 'deleted'
                   AND (@platform IS NULL OR pd.platform = @platform)
                   AND (@userType IS NULL OR (@userType = 'registered' AND p.user_id IS NOT NULL) OR (@userType = 'guest' AND p.user_id IS NULL))
                   AND (@categoryId IS NULL OR p.category_id = @categoryId)) AS posts_created,
                (SELECT COUNT(*)
                 FROM comments c
                 JOIN devices cd ON cd.id = c.device_id
                 JOIN posts cp ON cp.id = c.post_id
                 WHERE c.created_at >= @from AND c.created_at < @to
                   AND c.status <> 'deleted'
                   AND (@platform IS NULL OR cd.platform = @platform)
                   AND (@userType IS NULL OR (@userType = 'registered' AND c.user_id IS NOT NULL) OR (@userType = 'guest' AND c.user_id IS NULL))
                   AND (@categoryId IS NULL OR cp.category_id = @categoryId)) AS comments_created,
                (SELECT COUNT(*)
                 FROM votes v
                 JOIN devices vd ON vd.id = v.device_id
                 JOIN posts vp ON vp.id = v.post_id
                 WHERE v.created_at >= @from AND v.created_at < @to
                   AND (@platform IS NULL OR vd.platform = @platform)
                   AND (@userType IS NULL OR (@userType = 'registered' AND EXISTS (SELECT 1 FROM users u WHERE u.device_id = v.device_id AND u.deleted_at IS NULL)) OR (@userType = 'guest' AND NOT EXISTS (SELECT 1 FROM users u WHERE u.device_id = v.device_id AND u.deleted_at IS NULL)))
                   AND (@categoryId IS NULL OR vp.category_id = @categoryId)) AS votes_created,
                (SELECT COUNT(*)
                 FROM discover_events de
                 JOIN posts dp ON dp.id = de.post_id
                 LEFT JOIN devices dd ON dd.id = de.device_id
                 WHERE de.created_at >= @from AND de.created_at < @to
                   AND de.event_type = 'impression'
                   AND (@platform IS NULL OR dd.platform = @platform)
                   AND (@userType IS NULL OR (@userType = 'registered' AND de.user_id IS NOT NULL) OR (@userType = 'guest' AND de.user_id IS NULL))
                   AND (@source IS NULL OR COALESCE(de.metadata->>'source', 'discover') = @source)
                   AND (@categoryId IS NULL OR dp.category_id = @categoryId)) AS feed_impressions
            """,
            connection);
        AddAnalyticsParameters(cmd, start, end, platform, userType, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        var activeDevices = Convert.ToInt64(reader.GetValue(0));
        var postsCreated = Convert.ToInt64(reader.GetValue(1));
        var commentsCreated = Convert.ToInt64(reader.GetValue(2));
        var votesCreated = Convert.ToInt64(reader.GetValue(3));
        var feedImpressions = Convert.ToInt64(reader.GetValue(4));

        return new
        {
            activeDevices,
            postsCreated,
            commentsCreated,
            votesCreated,
            feedImpressions,
            postsPerActiveDevice = activeDevices == 0 ? 0.0 : Math.Round(postsCreated * 1.0 / activeDevices, 2),
            votesPerActiveDevice = activeDevices == 0 ? 0.0 : Math.Round(votesCreated * 1.0 / activeDevices, 2),
        };
    }

    var current = await ReadSummaryAsync(range.Value.From, range.Value.To);
    var previous = await ReadSummaryAsync(range.Value.PreviousFrom, range.Value.PreviousTo);

    await using var dauMauCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(DISTINCT CASE WHEN last_seen_at >= CURRENT_DATE THEN id END) AS dau_today,
            COUNT(DISTINCT CASE WHEN last_seen_at >= DATE_TRUNC('month', NOW()) THEN id END) AS mau_this_month
        FROM devices
        WHERE (@platform IS NULL OR platform = @platform)
        """,
        connection);
    dauMauCmd.Parameters.Add("platform", NpgsqlDbType.Text).Value =
        string.IsNullOrWhiteSpace(platform) ? DBNull.Value : (object)platform;
    await using var dauMauReader = await dauMauCmd.ExecuteReaderAsync();
    await dauMauReader.ReadAsync();
    var dauToday      = Convert.ToInt64(dauMauReader.GetValue(0));
    var mauThisMonth  = Convert.ToInt64(dauMauReader.GetValue(1));
    var dauToMauRatio = mauThisMonth == 0 ? 0.0 : Math.Round(dauToday * 100.0 / mauThisMonth, 1);
    await dauMauReader.CloseAsync();

    await using var seriesCmd = new NpgsqlCommand(
        $"""
        WITH buckets AS (
            SELECT generate_series(@from, @to - @step::interval, @step::interval) AS bucket
        )
        SELECT
            b.bucket,
            COALESCE(COUNT(DISTINCT d.id), 0) AS active_devices,
            COALESCE(COUNT(DISTINCT p.id), 0) AS posts_created,
            COALESCE(COUNT(DISTINCT c.id), 0) AS comments_created,
            COALESCE(COUNT(v.post_id), 0) AS votes_created
        FROM buckets b
        LEFT JOIN devices d
          ON date_trunc(@groupBy, d.last_seen_at) = b.bucket
         AND d.last_seen_at >= @from AND d.last_seen_at < @to
         AND (@platform IS NULL OR d.platform = @platform)
        LEFT JOIN posts p
          ON date_trunc(@groupBy, p.created_at) = b.bucket
         AND p.status <> 'deleted'
         AND (@categoryId IS NULL OR p.category_id = @categoryId)
        LEFT JOIN comments c
          ON date_trunc(@groupBy, c.created_at) = b.bucket
         AND c.status <> 'deleted'
        LEFT JOIN votes v
          ON date_trunc(@groupBy, v.created_at) = b.bucket
        GROUP BY b.bucket
        ORDER BY b.bucket
        """,
        connection);
    AddAnalyticsParameters(seriesCmd, range.Value.From, range.Value.To, platform, userType, source, categoryId);
    seriesCmd.Parameters.AddWithValue("groupBy", range.Value.GroupBy);
    seriesCmd.Parameters.AddWithValue("step", range.Value.Step);

    var series = new List<object>();
    await using var seriesReader = await seriesCmd.ExecuteReaderAsync();
    while (await seriesReader.ReadAsync())
    {
        series.Add(new
        {
            bucket = seriesReader.GetFieldValue<DateTimeOffset>(0),
            activeDevices = Convert.ToInt64(seriesReader.GetValue(1)),
            postsCreated = Convert.ToInt64(seriesReader.GetValue(2)),
            commentsCreated = Convert.ToInt64(seriesReader.GetValue(3)),
            votesCreated = Convert.ToInt64(seriesReader.GetValue(4)),
        });
    }

    return Results.Ok(new
    {
        from = range.Value.From,
        to = range.Value.To,
        groupBy = range.Value.GroupBy,
        filters     = new { platform, userType, source, categoryId },
        dauToday,
        mauThisMonth,
        dauToMauRatio,
        current,
        previous,
        series,
    });
});

app.MapGet("/api/v1/admin/analytics/funnels", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    string type = "judgment",
    DateTimeOffset? from = null,
    DateTimeOffset? to = null,
    string? platform = null,
    string? source = null,
    int? categoryId = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var range = NormalizeAnalyticsRange(from, to, "day");
    if (range is null)
        return BadRequest("INVALID_RANGE", "Tarih araligi gecersiz.");

    type = type.ToLowerInvariant();
    if (type is not ("judgment" or "creation" or "growth"))
        return BadRequest("INVALID_FUNNEL", "Funnel tipi judgment, creation veya growth olmali.");

    await using var connection = await db.OpenConnectionAsync();
    var steps = new List<object>();
    double? publishRate = null;
    double? holdRate = null;
    double? dropoutRate = null;

    if (type is "judgment")
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) FILTER (WHERE de.event_type = 'impression') AS impressions,
                COUNT(*) FILTER (WHERE de.event_type = 'dwell') AS dwell,
                COUNT(*) FILTER (WHERE de.event_type = 'vote') AS votes,
                COUNT(*) FILTER (WHERE de.event_type = 'comment_open') AS verdict_views
            FROM discover_events de
            JOIN posts p ON p.id = de.post_id
            LEFT JOIN devices d ON d.id = de.device_id
            WHERE de.created_at >= @from AND de.created_at < @to
              AND (@platform IS NULL OR d.platform = @platform)
              AND (@source IS NULL OR COALESCE(de.metadata->>'source', 'discover') = @source)
              AND (@categoryId IS NULL OR p.category_id = @categoryId)
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, null, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        steps.Add(new { key = "impression",     label = "Impressions",        count = Convert.ToInt64(reader.GetValue(0)) });
        steps.Add(new { key = "dwell",          label = "Meaningful dwell",   count = Convert.ToInt64(reader.GetValue(1)) });
        steps.Add(new { key = "vote",           label = "Votes",              count = Convert.ToInt64(reader.GetValue(2)) });
        steps.Add(new { key = "verdict_viewed", label = "Verdict viewed proxy", count = Convert.ToInt64(reader.GetValue(3)) });
    }
    else if (type is "creation")
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) AS submitted,
                COUNT(*) FILTER (WHERE status = 'active') AS published,
                COUNT(*) FILTER (WHERE status IN ('under_review', 'auto_hidden')) AS held_for_review
            FROM posts p
            JOIN devices d ON d.id = p.device_id
            WHERE p.created_at >= @from AND p.created_at < @to
              AND (@platform IS NULL OR d.platform = @platform)
              AND (@categoryId IS NULL OR p.category_id = @categoryId)
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, null, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var submitted = Convert.ToInt64(reader.GetValue(0));
        var published = Convert.ToInt64(reader.GetValue(1));
        var held      = Convert.ToInt64(reader.GetValue(2));
        publishRate = submitted == 0 ? 0.0 : Math.Round(published * 100.0 / submitted, 1);
        holdRate    = submitted == 0 ? 0.0 : Math.Round(held      * 100.0 / submitted, 1);
        dropoutRate = submitted == 0 ? 0.0 : Math.Round((submitted - published - held) * 100.0 / submitted, 1);
        steps.Add(new { key = "create_post_started", label = "Create started proxy", count = submitted });
        steps.Add(new { key = "submitted",            label = "Submitted",           count = submitted });
        steps.Add(new { key = "published",            label = "Published",           count = published });
        steps.Add(new { key = "held_for_review",      label = "Held for review",     count = held });
    }
    else
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) FILTER (WHERE event_type = 'share_landing_opened') AS opened,
                COUNT(*) FILTER (WHERE event_type = 'share_landing_vote_attempt') AS vote_attempt,
                COUNT(*) FILTER (WHERE event_type = 'share_landing_completed_judgment') AS completed,
                COUNT(*) FILTER (WHERE event_type = 'share_to_install') AS install
            FROM growth_events
            WHERE created_at >= @from AND created_at < @to
              AND (@platform IS NULL OR platform = @platform)
              AND (@source IS NULL OR source = @source)
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, null, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        steps.Add(new { key = "share_landing_opened",              label = "Share landing opened", count = Convert.ToInt64(reader.GetValue(0)) });
        steps.Add(new { key = "share_landing_vote_attempt",        label = "Vote attempted",       count = Convert.ToInt64(reader.GetValue(1)) });
        steps.Add(new { key = "share_landing_completed_judgment",  label = "Completed judgment",   count = Convert.ToInt64(reader.GetValue(2)) });
        steps.Add(new { key = "share_to_install",                  label = "Install",              count = Convert.ToInt64(reader.GetValue(3)) });
    }

    return Results.Ok(new
    {
        type,
        from = range.Value.From,
        to = range.Value.To,
        filters    = new { platform, source, categoryId },
        steps,
        publishRate,
        holdRate,
        dropoutRate,
    });
});

app.MapGet("/api/v1/admin/analytics/reports/timeseries", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    DateTimeOffset? from = null,
    DateTimeOffset? to = null,
    string groupBy = "day",
    string? reason = null,
    string? status = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var range = NormalizeAnalyticsRange(from, to, groupBy);
    if (range is null)
        return BadRequest("INVALID_RANGE", "Tarih araligi veya groupBy gecersiz.");

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        WITH buckets AS (
            SELECT generate_series(@from, @to - @step::interval, @step::interval) AS bucket
        ),
        daily AS (
            SELECT
                date_trunc(@groupBy, created_at) AS bucket,
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE status = 'pending') AS pending,
                COUNT(*) FILTER (WHERE status = 'actioned') AS actioned,
                COUNT(*) FILTER (WHERE status = 'dismissed') AS dismissed
            FROM reports
            WHERE created_at >= @from AND created_at < @to
              AND (@reason IS NULL OR reason = @reason)
              AND (@status IS NULL OR status = @status)
            GROUP BY date_trunc(@groupBy, created_at)
        )
        SELECT
            b.bucket,
            COALESCE(d.total, 0),
            COALESCE(d.pending, 0),
            COALESCE(d.actioned, 0),
            COALESCE(d.dismissed, 0)
        FROM buckets b
        LEFT JOIN daily d ON d.bucket = b.bucket
        ORDER BY b.bucket
        """,
        connection);
    cmd.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = range.Value.From;
    cmd.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = range.Value.To;
    cmd.Parameters.AddWithValue("groupBy", range.Value.GroupBy);
    cmd.Parameters.AddWithValue("step", range.Value.Step);
    cmd.Parameters.Add("reason", NpgsqlDbType.Text).Value = (object?)reason ?? DBNull.Value;
    cmd.Parameters.Add("status", NpgsqlDbType.Text).Value = (object?)status ?? DBNull.Value;

    var series = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        series.Add(new
        {
            bucket = reader.GetFieldValue<DateTimeOffset>(0),
            total = Convert.ToInt64(reader.GetValue(1)),
            pending = Convert.ToInt64(reader.GetValue(2)),
            actioned = Convert.ToInt64(reader.GetValue(3)),
            dismissed = Convert.ToInt64(reader.GetValue(4)),
        });
    }

    return Results.Ok(new
    {
        from = range.Value.From,
        to = range.Value.To,
        groupBy = range.Value.GroupBy,
        filters = new { reason, status },
        series,
    });
});

app.MapGet("/api/v1/admin/analytics/export", async (
    HttpRequest httpRequest,
    HttpResponse httpResponse,
    Db db,
    AdminAuthService adminAuth,
    string report = "activity",
    string format = "csv",
    DateTimeOffset? from = null,
    DateTimeOffset? to = null,
    string groupBy = "day",
    string? platform = null,
    string? userType = null,
    string? source = null,
    int? categoryId = null
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

    report = report.ToLowerInvariant().Replace("-", "_");
    format = format.ToLowerInvariant();
    if (report is not ("activity" or "moderation" or "growth"))
        return BadRequest("INVALID_REPORT", "Export raporu activity, moderation veya growth olmali.");
    if (format is not ("csv" or "json"))
        return BadRequest("INVALID_FORMAT", "Export format csv veya json olmali.");

    var range = NormalizeAnalyticsRange(from, to, groupBy);
    if (range is null)
        return BadRequest("INVALID_RANGE", "Tarih araligi veya groupBy gecersiz.");

    await using var connection = await db.OpenConnectionAsync();
    var rows = new List<Dictionary<string, object?>>();

    if (report is "activity")
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT 'posts' AS metric, COUNT(*)::bigint AS value
            FROM posts p JOIN devices d ON d.id = p.device_id
            WHERE p.created_at >= @from AND p.created_at < @to
              AND p.status <> 'deleted'
              AND (@platform IS NULL OR d.platform = @platform)
              AND (@userType IS NULL OR (@userType = 'registered' AND p.user_id IS NOT NULL) OR (@userType = 'guest' AND p.user_id IS NULL))
              AND (@categoryId IS NULL OR p.category_id = @categoryId)
            UNION ALL
            SELECT 'comments', COUNT(*)::bigint
            FROM comments c JOIN devices d ON d.id = c.device_id
            WHERE c.created_at >= @from AND c.created_at < @to
              AND c.status <> 'deleted'
              AND (@platform IS NULL OR d.platform = @platform)
              AND (@userType IS NULL OR (@userType = 'registered' AND c.user_id IS NOT NULL) OR (@userType = 'guest' AND c.user_id IS NULL))
            UNION ALL
            SELECT 'votes', COUNT(*)::bigint
            FROM votes v JOIN devices d ON d.id = v.device_id
            WHERE v.created_at >= @from AND v.created_at < @to
              AND (@platform IS NULL OR d.platform = @platform)
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, userType, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Dictionary<string, object?> { ["metric"] = reader.GetString(0), ["value"] = Convert.ToInt64(reader.GetValue(1)) });
    }
    else if (report is "moderation")
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT reason, status, COUNT(*)::bigint AS value
            FROM reports
            WHERE created_at >= @from AND created_at < @to
            GROUP BY reason, status
            ORDER BY value DESC
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, userType, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["reason"] = reader.GetString(0),
                ["status"] = reader.GetString(1),
                ["value"] = Convert.ToInt64(reader.GetValue(2)),
            });
        }
    }
    else
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_type, COALESCE(source, 'unknown') AS source, COALESCE(platform, 'unknown') AS platform, COUNT(*)::bigint AS value
            FROM growth_events
            WHERE created_at >= @from AND created_at < @to
              AND (@platform IS NULL OR platform = @platform)
              AND (@source IS NULL OR source = @source)
            GROUP BY event_type, source, platform
            ORDER BY value DESC
            """,
            connection);
        AddAnalyticsParameters(cmd, range.Value.From, range.Value.To, platform, userType, source, categoryId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["eventType"] = reader.GetString(0),
                ["source"] = reader.GetString(1),
                ["platform"] = reader.GetString(2),
                ["value"] = Convert.ToInt64(reader.GetValue(3)),
            });
        }
    }

    await using var auditTx = await connection.BeginTransactionAsync();
    await LogAdminActionAsync(
        connection,
        auditTx,
        adminEmail,
        "analytics_exported",
        "analytics",
        null,
        $"report={report}, from={range.Value.From:O}, to={range.Value.To:O}, format={format}");
    await auditTx.CommitAsync();

    httpResponse.Headers["Content-Disposition"] =
        $"attachment; filename=\"{report}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{format}\"";

    if (format is "json")
    {
        var json = JsonSerializer.Serialize(new
        {
            report,
            from = range.Value.From,
            to = range.Value.To,
            filters = new { platform, userType, source, categoryId },
            rows,
        });
        return Results.Text(json, "application/json; charset=utf-8", Encoding.UTF8);
    }

    var csv = BuildDictionaryCsv(rows);
    return Results.Text(csv, "text/csv; charset=utf-8", Encoding.UTF8);
}).RequireRateLimiting("admin-analytics-export");

app.MapPost("/api/v1/admin/analytics/scheduled-reports", async (
    AdminScheduledReportRequest request,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var report = request.Report.ToLowerInvariant().Replace("-", "_");
    var frequency = request.Frequency.ToLowerInvariant();
    var format = request.Format.ToLowerInvariant();
    if (report is not ("overview" or "activity" or "creation" or "feed_quality" or "moderation" or "notifications" or "growth" or "operations"))
        return BadRequest("INVALID_REPORT", "Gecersiz scheduled report tipi.");
    if (frequency is not ("daily" or "weekly"))
        return BadRequest("INVALID_FREQUENCY", "Frequency daily veya weekly olmali.");
    if (format is not ("csv" or "json"))
        return BadRequest("INVALID_FORMAT", "Format csv veya json olmali.");

    var filtersJson = JsonSerializer.Serialize(new
    {
        request.Platform,
        request.UserType,
        request.Source,
        request.CategoryId,
    });

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        INSERT INTO admin_scheduled_reports (
            name, report, frequency, format, timezone, filters, created_by
        ) VALUES (
            @name, @report, @frequency, @format, @timezone, @filters::jsonb, @createdBy
        )
        RETURNING id, created_at
        """,
        connection,
        transaction);
    cmd.Parameters.Add("name", NpgsqlDbType.Text).Value = (object?)request.Name ?? DBNull.Value;
    cmd.Parameters.AddWithValue("report", report);
    cmd.Parameters.AddWithValue("frequency", frequency);
    cmd.Parameters.AddWithValue("format", format);
    cmd.Parameters.AddWithValue("timezone", string.IsNullOrWhiteSpace(request.Timezone) ? "Europe/Istanbul" : request.Timezone);
    cmd.Parameters.AddWithValue("filters", filtersJson);
    cmd.Parameters.AddWithValue("createdBy", adminEmail);

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    var id = reader.GetGuid(0);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(1);
    await reader.CloseAsync();

    await LogAdminActionAsync(
        connection,
        transaction,
        adminEmail,
        "analytics_scheduled_report_created",
        "analytics",
        id,
        $"report={report}, frequency={frequency}, format={format}");
    await transaction.CommitAsync();

    return Results.Created($"/api/v1/admin/analytics/scheduled-reports/{id}", new
    {
        id,
        report,
        frequency,
        format,
        createdAt,
        isActive = true,
    });
}).RequireRateLimiting("admin-analytics-export");

app.MapGet("/api/v1/admin/analytics/scheduled-reports", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT id, name, report, frequency, format, timezone, filters, created_by, created_at, is_active
        FROM admin_scheduled_reports
        WHERE is_active = TRUE
        ORDER BY created_at DESC
        LIMIT 100
        """,
        connection);

    var items = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.IsDBNull(1) ? null : reader.GetString(1),
            report = reader.GetString(2),
            frequency = reader.GetString(3),
            format = reader.GetString(4),
            timezone = reader.GetString(5),
            filters = reader.GetString(6),
            createdBy = reader.GetString(7),
            createdAt = reader.GetFieldValue<DateTimeOffset>(8),
            isActive = reader.GetBoolean(9),
        });
    }

    return Results.Ok(new { items });
});

app.MapDelete("/api/v1/admin/analytics/scheduled-reports/{id:guid}", async (
    Guid id,
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        UPDATE admin_scheduled_reports
        SET is_active = FALSE
        WHERE id = @id AND is_active = TRUE
        RETURNING id
        """,
        connection,
        transaction);
    cmd.Parameters.AddWithValue("id", id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        await transaction.RollbackAsync();
        return Results.NotFound(new { error = "REPORT_NOT_FOUND", message = "Scheduled report bulunamadı." });
    }
    await reader.CloseAsync();

    await LogAdminActionAsync(
        connection,
        transaction,
        adminEmail,
        "analytics_scheduled_report_deleted",
        "analytics",
        id,
        $"id={id}");
    await transaction.CommitAsync();

    return Results.NoContent();
});

// ── GROWTH EVENTS INGESTION ──────────────────────────────────────────────────

app.MapPost("/api/v1/growth-events", async (
    GrowthEventRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    JwtService jwtService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    if (request.EventType is not (
        "share_landing_opened" or
        "share_landing_vote_attempt" or
        "share_landing_completed_judgment" or
        "share_to_install" or
        "notification_completed_judgment" or
        "feed_completed_judgment" or
        "search_completed_judgment"))
    {
        return BadRequest("INVALID_GROWTH_EVENT", "Geçersiz growth event tipi.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO growth_events (
            post_id, device_id, user_id, event_type, source, platform, referrer_code
        ) VALUES (
            @postId, @deviceId, @userId, @eventType, @source, @platform, @referrerCode
        )
        """,
        connection
    );
    command.Parameters.Add("postId", NpgsqlDbType.Uuid).Value = (object?)request.PostId ?? DBNull.Value;
    command.Parameters.Add("deviceId", NpgsqlDbType.Uuid).Value = (object?)deviceId ?? DBNull.Value;
    command.Parameters.Add("userId", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
    command.Parameters.AddWithValue("eventType", request.EventType);
    command.Parameters.Add("source", NpgsqlDbType.Text).Value = (object?)request.Source ?? DBNull.Value;
    command.Parameters.Add("platform", NpgsqlDbType.Text).Value = (object?)request.Platform ?? DBNull.Value;
    command.Parameters.Add("referrerCode", NpgsqlDbType.Text).Value = (object?)request.ReferrerCode ?? DBNull.Value;
    await command.ExecuteNonQueryAsync();

    return Results.NoContent();
}).RequireRateLimiting("growth-events");

// ── NORTH-STAR: COMPLETED JUDGMENT LOOP ─────────────────────────────────────

app.MapPost("/api/v1/analytics/loop-completed", async (
    LoopCompletedRequest request,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice
) =>
{
    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO judgment_loop_events (
            device_id, post_id, source, loop_duration_seconds, dwell_seconds
        ) VALUES (
            @deviceId, @postId, @source, @loopDurationSeconds, @dwellSeconds
        )
        """,
        connection
    );
    command.Parameters.Add("deviceId", NpgsqlDbType.Text).Value = (object?)deviceId?.ToString() ?? DBNull.Value;
    command.Parameters.AddWithValue("postId", request.PostId);
    command.Parameters.AddWithValue("source", request.Source);
    command.Parameters.AddWithValue("loopDurationSeconds", request.LoopDurationSeconds);
    command.Parameters.AddWithValue("dwellSeconds", request.DwellSeconds);
    await command.ExecuteNonQueryAsync();

    return Results.NoContent();
}).RequireRateLimiting("growth-events");

// ── GROWTH ANALYTICS ────────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/growth", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var growthCmd = new NpgsqlCommand(
        """
        SELECT event_type, COUNT(*) AS cnt
        FROM growth_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        GROUP BY event_type
        """,
        connection);
    growthCmd.Parameters.AddWithValue("days", days);

    var growthFunnel = new Dictionary<string, long>(StringComparer.Ordinal);
    await using var growthReader = await growthCmd.ExecuteReaderAsync();
    while (await growthReader.ReadAsync())
        growthFunnel[growthReader.GetString(0)] = Convert.ToInt64(growthReader.GetValue(1));
    await growthReader.CloseAsync();

    await using var discoverCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE event_type = 'impression') AS impressions,
            COUNT(*) FILTER (WHERE event_type = 'dwell') AS dwell,
            COUNT(*) FILTER (WHERE event_type = 'vote') AS votes,
            COUNT(*) FILTER (WHERE event_type = 'share') AS shares
        FROM discover_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    discoverCmd.Parameters.AddWithValue("days", days);

    await using var discoverReader = await discoverCmd.ExecuteReaderAsync();
    await discoverReader.ReadAsync();
    var discoverImpressions = Convert.ToInt64(discoverReader.GetValue(0));
    var discoverDwell = Convert.ToInt64(discoverReader.GetValue(1));
    var discoverVotes = Convert.ToInt64(discoverReader.GetValue(2));
    var discoverShares = Convert.ToInt64(discoverReader.GetValue(3));
    await discoverReader.CloseAsync();

    await using var notificationCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE event_type = 'sent') AS sent,
            COUNT(*) FILTER (WHERE event_type = 'opened') AS opened
        FROM notification_events
        WHERE occurred_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    notificationCmd.Parameters.AddWithValue("days", days);

    await using var notificationReader = await notificationCmd.ExecuteReaderAsync();
    await notificationReader.ReadAsync();
    var notificationSent = Convert.ToInt64(notificationReader.GetValue(0));
    var notificationOpened = Convert.ToInt64(notificationReader.GetValue(1));
    await notificationReader.CloseAsync();

    await using var activeDevicesCmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM devices WHERE last_seen_at >= NOW() - (@days * INTERVAL '1 day')",
        connection);
    activeDevicesCmd.Parameters.AddWithValue("days", days);
    var activeDevices = Convert.ToInt64(await activeDevicesCmd.ExecuteScalarAsync());

    await using var notifJudgmentCmd = new NpgsqlCommand(
        """
        SELECT COUNT(*) FROM growth_events
        WHERE event_type = 'notification_completed_judgment'
          AND created_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    notifJudgmentCmd.Parameters.AddWithValue("days", days);
    var notificationCompletedJudgments = Convert.ToInt64(await notifJudgmentCmd.ExecuteScalarAsync());

    long GetGrowth(string key) => growthFunnel.TryGetValue(key, out var value) ? value : 0;
    static double RatePercent(long numerator, long denominator) =>
        denominator == 0 ? 0.0 : Math.Round(numerator * 100.0 / denominator, 1);

    var shareLandingOpened = GetGrowth("share_landing_opened");
    var shareLandingVoteAttempt = GetGrowth("share_landing_vote_attempt");
    var shareLandingCompletedJudgment = GetGrowth("share_landing_completed_judgment");
    var shareToInstall = GetGrowth("share_to_install");
    var installConversion = shareLandingOpened == 0 ? 0.0 : shareToInstall / (double)shareLandingOpened;
    var kFactorEstimate = activeDevices == 0 ? 0.0 : Math.Round(discoverShares * installConversion / activeDevices, 3);

    return Results.Ok(new
    {
        days,
        growthFunnel = new
        {
            shareLandingOpened,
            shareLandingVoteAttempt,
            shareLandingCompletedJudgment,
            shareToInstall,
        },
        shareLandingVoteAttemptRate = RatePercent(shareLandingVoteAttempt, shareLandingOpened),
        shareToCompletedJudgmentRate = RatePercent(shareLandingCompletedJudgment, shareLandingOpened),
        shareToInstallRate = RatePercent(shareToInstall, shareLandingOpened),
        kFactorEstimate,
        discover = new
        {
            impressions = discoverImpressions,
            dwell = discoverDwell,
            votes = discoverVotes,
            shares = discoverShares,
            dwellRate = RatePercent(discoverDwell, discoverImpressions),
            discoverToCompletedJudgment = RatePercent(discoverVotes, discoverImpressions),
        },
        notifications = new
        {
            sent = notificationSent,
            opened = notificationOpened,
            openRate = RatePercent(notificationOpened, notificationSent),
            notificationCompletedJudgments,
            notificationToCompletedJudgment = RatePercent(notificationCompletedJudgments, notificationOpened),
        },
        dataQuality = new
        {
            growthEventsTable = "growth_events",
            firebaseExportRequiredForCrossDeviceAttribution = true,
            notificationCompletedJudgmentNeedsClientSourceAttribution = true,
        },
    });
});

// ── NORTH-STAR ANALYTICS ────────────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/north-star", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth,
    int days = 7
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    days = Math.Clamp(days, 1, 90);
    await using var connection = await db.OpenConnectionAsync();

    await using var discoverCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE event_type = 'impression') AS impressions,
            COUNT(*) FILTER (WHERE event_type = 'dwell' AND COALESCE(dwell_seconds, 0) >= 15) AS meaningful_dwell,
            COUNT(*) FILTER (WHERE event_type = 'vote') AS votes,
            COUNT(*) FILTER (WHERE event_type = 'comment_open') AS comment_opens
        FROM discover_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    discoverCmd.Parameters.AddWithValue("days", days);

    await using var discoverReader = await discoverCmd.ExecuteReaderAsync();
    await discoverReader.ReadAsync();
    var discoverImpressions = Convert.ToInt64(discoverReader.GetValue(0));
    var discoverMeaningfulDwell = Convert.ToInt64(discoverReader.GetValue(1));
    var discoverVotes = Convert.ToInt64(discoverReader.GetValue(2));
    var discoverCommentOpens = Convert.ToInt64(discoverReader.GetValue(3));
    await discoverReader.CloseAsync();

    await using var growthCmd = new NpgsqlCommand(
        """
        SELECT
            COUNT(*) FILTER (WHERE event_type = 'share_landing_opened') AS share_landing_opened,
            COUNT(*) FILTER (WHERE event_type = 'share_landing_completed_judgment') AS share_landing_completed_judgment,
            COUNT(*) FILTER (WHERE event_type = 'notification_completed_judgment') AS notification_completed_judgment,
            COUNT(*) FILTER (WHERE event_type = 'feed_completed_judgment') AS feed_completed_judgment,
            COUNT(*) FILTER (WHERE event_type = 'search_completed_judgment') AS search_completed_judgment
        FROM growth_events
        WHERE created_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    growthCmd.Parameters.AddWithValue("days", days);

    await using var growthReader = await growthCmd.ExecuteReaderAsync();
    await growthReader.ReadAsync();
    var shareLandingOpened = Convert.ToInt64(growthReader.GetValue(0));
    var shareLandingCompletedJudgment = Convert.ToInt64(growthReader.GetValue(1));
    var notificationCompletedJudgments = Convert.ToInt64(growthReader.GetValue(2));
    var feedCompletedJudgments = Convert.ToInt64(growthReader.GetValue(3));
    var searchCompletedJudgments = Convert.ToInt64(growthReader.GetValue(4));
    await growthReader.CloseAsync();

    await using var notificationCmd = new NpgsqlCommand(
        """
        SELECT COUNT(*) FILTER (WHERE event_type = 'opened') AS opened
        FROM notification_events
        WHERE occurred_at >= NOW() - (@days * INTERVAL '1 day')
        """,
        connection);
    notificationCmd.Parameters.AddWithValue("days", days);
    var notificationOpened = Convert.ToInt64(await notificationCmd.ExecuteScalarAsync());

    static double Rate(long numerator, long denominator) =>
        denominator == 0 ? 0.0 : Math.Round(numerator * 100.0 / denominator, 1);

    var backendVerdictViewedProxy = discoverVotes + shareLandingCompletedJudgment
        + notificationCompletedJudgments + feedCompletedJudgments + searchCompletedJudgments;
    var entryPoints = discoverImpressions + shareLandingOpened + notificationOpened;

    return Results.Ok(new
    {
        days,
        weeklyCompletedJudgmentLoops = backendVerdictViewedProxy,
        entryPoints,
        completionRate = Rate(backendVerdictViewedProxy, entryPoints),
        sourceBreakdown = new
        {
            discover = discoverVotes,
            shareLanding = shareLandingCompletedJudgment,
            notification = notificationCompletedJudgments,
            feed = feedCompletedJudgments,
            search = searchCompletedJudgments,
        },
        discover = new
        {
            impressions = discoverImpressions,
            meaningfulDwell = discoverMeaningfulDwell,
            votes = discoverVotes,
            commentOpens = discoverCommentOpens,
            meaningfulDwellRate = Rate(discoverMeaningfulDwell, discoverImpressions),
            discoverToCompletedJudgment = Rate(discoverVotes, discoverImpressions),
        },
        share = new
        {
            landingOpened = shareLandingOpened,
            completedJudgments = shareLandingCompletedJudgment,
            shareToCompletedJudgment = Rate(shareLandingCompletedJudgment, shareLandingOpened),
        },
        notifications = new
        {
            opened = notificationOpened,
            completedJudgments = notificationCompletedJudgments,
            notificationToCompletedJudgment = Rate(notificationCompletedJudgments, notificationOpened),
        },
        feed = new
        {
            completedJudgments = feedCompletedJudgments,
        },
        search = new
        {
            completedJudgments = searchCompletedJudgments,
        },
        dataQuality = new
        {
            northStarMetric = "weekly_completed_judgment_loops",
            backendProxyUsesVoteAsVerdictViewed = true,
            firebaseVerdictViewedExportRequired = true,
            meaningfulDwellThresholdSeconds = 15,
            sourcesTracked = new[] { "feed", "discover", "share_landing", "notification", "search" },
        },
    });
});

// ── Admin Analytics — Operations ─────────────────────────────────────────────

app.MapGet("/api/v1/admin/analytics/operations", async (
    HttpRequest httpRequest,
    AdminAuthService adminAuth,
    SloMetrics sloMetrics,
    RedisService redis,
    IConfiguration configuration,
    IHostEnvironment environment
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized)
        return unauthorized;

    var sloSnapshot = sloMetrics.GetSnapshot();

    var (cacheHits, cacheMisses) = await redis.GetCacheMetricsAsync();
    var cacheTotal = cacheHits + cacheMisses;
    var cacheHitRate = cacheTotal == 0 ? 0.0 : Math.Round(cacheHits * 100.0 / cacheTotal, 1);

    var commitSha =
        configuration["Build:CommitSha"] ??
        Environment.GetEnvironmentVariable("GIT_COMMIT_SHA") ??
        Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ??
        Environment.GetEnvironmentVariable("VERCEL_GIT_COMMIT_SHA") ??
        Environment.GetEnvironmentVariable("SOURCE_VERSION") ??
        Environment.GetEnvironmentVariable("COMMIT_SHA") ??
        "unknown";
    var deployedAt =
        configuration["Build:DeployedAt"] ??
        Environment.GetEnvironmentVariable("DEPLOYED_AT") ??
        "unknown";

    return Results.Ok(new
    {
        slo = new
        {
            status = sloSnapshot.Status,
            windowSeconds = (int)sloSnapshot.Window.TotalSeconds,
            checks = sloSnapshot.Checks.Select(c => new
            {
                name = c.Name,
                status = c.Status,
                value = c.Value,
                target = c.Target,
                unit = c.Unit,
                sampleCount = c.SampleCount,
            }),
            burnRates = sloSnapshot.BurnRatePolicies.Select(b => new
            {
                name = b.Name,
                severity = b.Severity,
                status = b.Status,
                burnRate = Math.Round(b.BurnRate, 3),
                threshold = b.Threshold,
                sampleCount = b.SampleCount,
            }),
        },
        cache = new
        {
            hits = cacheHits,
            misses = cacheMisses,
            total = cacheTotal,
            hitRatePercent = cacheHitRate,
            targetPercent = 85.0,
            isHealthy = cacheTotal < 100 || cacheHitRate >= 85.0,
        },
        deploy = new
        {
            commitSha,
            deployedAt,
            environment = environment.EnvironmentName,
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        },
        backgroundJobs = new[]
        {
            "TrendScoreUpdater",
            "NotificationDispatcher",
            "PostDistributionJob",
            "CommentNotificationBatcher",
            "ViralNotificationJob",
            "DataRetentionService",
            "ImageModerationWorker",
            "VerdictReminderJob",
            "BrigadeCoordinatedDetectorJob",
            "AuditLogExportJob",
            "DeferredNotificationFlushJob",
            "BurnRateAlertWorker",
        },
    });
});

// ── 2FA Yedek Kodları ────────────────────────────────────────────────────────

app.MapPost("/api/v1/auth/2fa/backup-codes", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var codes = Enumerable.Range(0, 8)
        .Select(_ => $"{GenerateBackupSegment()}-{GenerateBackupSegment()}")
        .ToList();

    await using var connection = await db.OpenConnectionAsync();
    await using var tx = await connection.BeginTransactionAsync();

    await using var deleteOld = new NpgsqlCommand(
        "DELETE FROM totp_backup_codes WHERE user_id = @userId",
        connection, tx
    );
    deleteOld.Parameters.AddWithValue("userId", userId);
    await deleteOld.ExecuteNonQueryAsync();

    foreach (var code in codes)
    {
        await using var insert = new NpgsqlCommand(
            "INSERT INTO totp_backup_codes (user_id, code_hash) VALUES (@userId, @hash)",
            connection, tx
        );
        insert.Parameters.AddWithValue("userId", userId);
        insert.Parameters.AddWithValue("hash", HashBackupCode(code));
        await insert.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    return Results.Ok(new { codes });
}).RequireRateLimiting("auth-strict");

app.MapGet("/api/v1/auth/2fa/backup-codes/count", async (
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
) =>
{
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT COUNT(*) FROM totp_backup_codes WHERE user_id = @userId AND used_at IS NULL",
        connection
    );
    cmd.Parameters.AddWithValue("userId", userId);
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    return Results.Ok(new { remaining = count });
});

// â"€â"€ E-posta DeÄŸiÅŸtirme â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/auth/change-email/request", async (
    ChangeEmailRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    EmailService emailService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } ve) return ve;
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var newEmail = request.NewEmail.ToLowerInvariant();
    await using var connection = await db.OpenConnectionAsync();

    await using var findCmd = new NpgsqlCommand(
        "SELECT password_hash, auth_provider FROM users WHERE id = @id AND deleted_at IS NULL",
        connection
    );
    findCmd.Parameters.AddWithValue("id", userId);
    await using var reader = await findCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return NotFound("USER_NOT_FOUND", "Kullanici bulunamadi.");
    var passwordHash = reader.IsDBNull(0) ? null : reader.GetString(0);
    var authProvider = reader.GetString(1);
    await reader.CloseAsync();

    if (authProvider != "password")
        return BadRequest("GOOGLE_ACCOUNT", "Google ile giris yapan hesaplarda e-posta degistirilemez.");

    if (passwordHash is null || !PasswordService.Verify(request.Password, passwordHash))
        return BadRequest("INVALID_PASSWORD", "Sifre hatali.");

    await using var checkCmd = new NpgsqlCommand(
        "SELECT id FROM users WHERE email = @email AND deleted_at IS NULL",
        connection
    );
    checkCmd.Parameters.AddWithValue("email", newEmail);
    if (await checkCmd.ExecuteScalarAsync() is not null)
        return Conflict("EMAIL_IN_USE", "Bu e-posta adresi zaten kullaniliyor.");

    var dbKey = $"chgmail:{newEmail}";

    // Redis: 3 dk cooldown, aynı pencerede aynı kod
    var (otp, tooSoon, waitSecs, isNewChgOtp) = await GetOrCreateCachedOtpAsync(
        redis.GetDb(), $"otp:chgemail:{newEmail}",
        validFor: TimeSpan.FromMinutes(10),
        resendAfter: TimeSpan.FromMinutes(3));

    if (tooSoon)
        return TooManyRequests("OTP_TOO_SOON", $"Yeni kod için {waitSecs} saniye bekleyin.", waitSecs);

    if (isNewChgOtp)
    {
        await using var delOld = new NpgsqlCommand("DELETE FROM email_otps WHERE email = @key", connection);
        delOld.Parameters.AddWithValue("key", dbKey);
        await delOld.ExecuteNonQueryAsync();

        await using var insertOtp = new NpgsqlCommand(
            "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)", connection);
        insertOtp.Parameters.AddWithValue("email", dbKey);
        insertOtp.Parameters.AddWithValue("hash", PasswordService.HashOtp(otp!));
        await insertOtp.ExecuteNonQueryAsync();
    }

    _ = emailService.SendChangeEmailOtpAsync(newEmail, otp!);
    return Results.Ok(new { message = "Dogrulama kodu yeni e-posta adresinize gonderildi." });
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/change-email/confirm", async (
    ConfirmChangeEmailRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService,
    RedisService redis
) =>
{
    if (ValidateRequest(request) is { } ve) return ve;
    var principal = GetJwtPrincipal(httpRequest, jwtService);
    if (principal is null) return Unauthorized();
    var userId = GetUserId(principal);

    var newEmail = request.NewEmail.ToLowerInvariant();
    var otpKey = $"chgmail:{newEmail}";
    var otpHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Otp)));

    await using var connection = await db.OpenConnectionAsync();
    await using var otpCmd = new NpgsqlCommand(
        "SELECT id, otp_hash, expires_at, attempts FROM email_otps WHERE email = @key ORDER BY created_at DESC LIMIT 1",
        connection
    );
    otpCmd.Parameters.AddWithValue("key", otpKey);
    await using var otpReader = await otpCmd.ExecuteReaderAsync();
    if (!await otpReader.ReadAsync())
        return BadRequest("OTP_NOT_FOUND", "Dogrulama kodu bulunamadi. Yeniden talep edin.");

    var otpId = otpReader.GetGuid(0);
    var storedHash = otpReader.GetString(1);
    var expiresAt = otpReader.GetFieldValue<DateTimeOffset>(2);
    var attempts = otpReader.GetInt32(3);
    await otpReader.CloseAsync();

    if (expiresAt < DateTimeOffset.UtcNow)
        return BadRequest("OTP_EXPIRED", "Kod suresi dolmus. Yeniden talep edin.");
    if (attempts >= 3)
        return BadRequest("OTP_MAX_ATTEMPTS", "Cok fazla hatali deneme. Yeni kod isteyin.");

    if (!string.Equals(storedHash, otpHash, StringComparison.OrdinalIgnoreCase))
    {
        await using var incCmd = new NpgsqlCommand(
            "UPDATE email_otps SET attempts = attempts + 1 WHERE id = @id", connection);
        incCmd.Parameters.AddWithValue("id", otpId);
        await incCmd.ExecuteNonQueryAsync();
        return BadRequest("OTP_INVALID", $"Kod hatali. {2 - attempts} deneme hakkin kaldi.");
    }

    await using var tx = await connection.BeginTransactionAsync();
    await using var updateEmail = new NpgsqlCommand(
        "UPDATE users SET email = @email, updated_at = NOW() WHERE id = @userId AND deleted_at IS NULL",
        connection, tx
    );
    updateEmail.Parameters.AddWithValue("email", newEmail);
    updateEmail.Parameters.AddWithValue("userId", userId);
    await updateEmail.ExecuteNonQueryAsync();

    await using var deleteOtp = new NpgsqlCommand("DELETE FROM email_otps WHERE email = @key", connection, tx);
    deleteOtp.Parameters.AddWithValue("key", otpKey);
    await deleteOtp.ExecuteNonQueryAsync();

    await tx.CommitAsync();
    await redis.GetDb().KeyDeleteAsync($"otp:chgemail:{newEmail}");
    return Results.NoContent();
}).RequireRateLimiting("auth-strict");

// â"€â"€ Hesap Kurtarma â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/auth/recover-account", async (
    RecoverAccountRequest request,
    Db db
) =>
{
    if (ValidateRequest(request) is { } ve) return ve;
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

    await using var connection = await db.OpenConnectionAsync();
    await using var findCmd = new NpgsqlCommand(
        "SELECT id, user_id, expires_at FROM account_recovery_tokens WHERE token_hash = @hash AND used_at IS NULL LIMIT 1",
        connection
    );
    findCmd.Parameters.AddWithValue("hash", tokenHash);
    await using var reader = await findCmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return BadRequest("INVALID_TOKEN", "Kurtarma baglantisi gecersiz veya daha once kullanildi.");

    var tokenId = reader.GetGuid(0);
    var recoveryUserId = reader.GetGuid(1);
    var expiresAt = reader.GetFieldValue<DateTimeOffset>(2);
    await reader.CloseAsync();

    if (expiresAt < DateTimeOffset.UtcNow)
        return BadRequest("TOKEN_EXPIRED", "Kurtarma baglantisi suresi dolmus.");

    await using var tx = await connection.BeginTransactionAsync();

    await using var restoreCmd = new NpgsqlCommand(
        "UPDATE users SET deleted_at = NULL, updated_at = NOW() WHERE id = @userId", connection, tx
    );
    restoreCmd.Parameters.AddWithValue("userId", recoveryUserId);
    await restoreCmd.ExecuteNonQueryAsync();

    await using var markUsed = new NpgsqlCommand(
        "UPDATE account_recovery_tokens SET used_at = NOW() WHERE id = @id", connection, tx
    );
    markUsed.Parameters.AddWithValue("id", tokenId);
    await markUsed.ExecuteNonQueryAsync();

    await tx.CommitAsync();
    return Results.Ok(new { message = "Hesabiniz basariyla geri alindi. Uygulamayi acarak giris yapabilirsiniz." });
}).RequireRateLimiting("auth-strict");

// ── ADMIN PLATFORM SETTINGS — RETENTION POLICY ────────────────────────────

app.MapGet("/api/v1/admin/settings/retention", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    await using var connection = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        """
        SELECT key, value, updated_at, updated_by
        FROM platform_settings
        WHERE key IN ('audit_log_retention_days', 'deleted_user_anonymization_days')
        """,
        connection
    );

    var settings = new Dictionary<string, string>();
    string? updatedAt = null;
    string? updatedBy = null;

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        settings[reader.GetString(0)] = reader.GetString(1);
        var rowUpdatedAt = reader.GetFieldValue<DateTimeOffset>(2).ToString("o");
        if (updatedAt is null || string.Compare(rowUpdatedAt, updatedAt, StringComparison.Ordinal) > 0)
        {
            updatedAt = rowUpdatedAt;
            updatedBy = reader.IsDBNull(3) ? null : reader.GetString(3);
        }
    }

    return Results.Ok(new
    {
        auditLogRetentionDays = settings.TryGetValue("audit_log_retention_days", out var v1) && int.TryParse(v1, out var d1) ? d1 : 365,
        deletedUserAnonymizationDays = settings.TryGetValue("deleted_user_anonymization_days", out var v2) && int.TryParse(v2, out var d2) ? d2 : 30,
        updatedAt,
        updatedBy
    });
});

app.MapPut("/api/v1/admin/settings/retention", async (
    HttpRequest httpRequest,
    Db db,
    AdminAuthService adminAuth
) =>
{
    if (RequireAdmin(httpRequest, adminAuth) is { } unauthorized) return unauthorized;

    RetentionSettingsRequest? body;
    try { body = await httpRequest.ReadFromJsonAsync<RetentionSettingsRequest>(); }
    catch { return BadRequest("INVALID_BODY", "Geçersiz istek gövdesi."); }

    if (body is null) return BadRequest("INVALID_BODY", "Geçersiz istek gövdesi.");
    if (body.AuditLogRetentionDays is < 30 or > 3650)
        return BadRequest("INVALID_VALUE", "Denetim logu saklama süresi 30-3650 gün arasında olmalı.");
    if (body.DeletedUserAnonymizationDays is < 30 or > 365)
        return BadRequest("INVALID_VALUE", "Kullanıcı anonimleştirme süresi 30-365 gün arasında olmalı (KVKK).");

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest) ?? "unknown";
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    await using var upsertCmd = new NpgsqlCommand(
        """
        INSERT INTO platform_settings (key, value, updated_at, updated_by) VALUES
            ('audit_log_retention_days',        @auditDays,      NOW(), @updatedBy),
            ('deleted_user_anonymization_days', @anonymizeDays,  NOW(), @updatedBy)
        ON CONFLICT (key) DO UPDATE
            SET value = EXCLUDED.value, updated_at = NOW(), updated_by = EXCLUDED.updated_by
        """,
        connection,
        transaction
    );
    upsertCmd.Parameters.AddWithValue("auditDays", body.AuditLogRetentionDays.ToString());
    upsertCmd.Parameters.AddWithValue("anonymizeDays", body.DeletedUserAnonymizationDays.ToString());
    upsertCmd.Parameters.AddWithValue("updatedBy", adminEmail);
    await upsertCmd.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "update_retention_settings", "platform", null,
        $"audit_log={body.AuditLogRetentionDays}d, user_anonymization={body.DeletedUserAnonymizationDays}d");
    await transaction.CommitAsync();

    return Results.Ok(new
    {
        auditLogRetentionDays = body.AuditLogRetentionDays,
        deletedUserAnonymizationDays = body.DeletedUserAnonymizationDays,
        updatedAt = DateTimeOffset.UtcNow.ToString("o"),
        updatedBy = adminEmail
    });
});

await app.RunAsync();

}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Uygulama beklenmedik şekilde sonlandı.");
}
finally
{
    Log.CloseAndFlush();
}

static IResult BadRequest(string code, string message) =>
    Results.BadRequest(new ErrorEnvelope(new ErrorBody(code, message)));

static IResult Conflict(string code, string message) =>
    Results.Conflict(new ErrorEnvelope(new ErrorBody(code, message)));

static IResult NotFound(string code, string message) =>
    Results.NotFound(new ErrorEnvelope(new ErrorBody(code, message)));

static IResult TooManyRequests(string code, string message, int? retryAfterSeconds = null) =>
    Results.Json(
        new ErrorEnvelope(new ErrorBody(code, message, retryAfterSeconds)),
        statusCode: StatusCodes.Status429TooManyRequests
    );

static IResult Unauthorized() =>
    Results.Unauthorized();

static IResult Forbid(string code, string message) =>
    Results.Json(new ErrorEnvelope(new ErrorBody(code, message)), statusCode: StatusCodes.Status403Forbidden);

static bool IsSupportedPlatform(string platform) => platform is "android" or "ios" or "web";

static string GetWebBaseUrl(IConfiguration configuration) =>
    (configuration["Web:BaseUrl"] ?? "https://karar.app").TrimEnd('/');

static bool IsCrawler(string userAgent)
{
    if (string.IsNullOrWhiteSpace(userAgent)) return false;

    return userAgent.Contains("facebookexternalhit", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("Twitterbot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("Googlebot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("LinkedInBot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("Slackbot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("TelegramBot", StringComparison.OrdinalIgnoreCase)
        || userAgent.Contains("Discordbot", StringComparison.OrdinalIgnoreCase);
}

static string ToSlug(string title, Guid id)
{
    var normalized = title.ToLowerInvariant()
        .Replace("ÄŸ", "g").Replace("Ã¼", "u").Replace("ÅŸ", "s")
        .Replace("Ä±", "i").Replace("Ã¶", "o").Replace("Ã§", "c")
        .Replace("Ä°", "i").Replace("Ãœ", "u").Replace("Å", "s")
        .Replace("Ä", "g").Replace("Ã–", "o").Replace("Ã‡", "c");
    normalized = Regex.Replace(normalized, @"[^a-z0-9\s-]", "");
    normalized = Regex.Replace(normalized, @"\s+", "-").Trim('-');
    if (normalized.Length > 50) normalized = normalized[..50].TrimEnd('-');
    return $"{id.ToString("N")[..8]}-{normalized}";
}

static string BuildOgHtml(
    Guid id, string slug, string title, string content,
    int hakli, int haksiz, string? customImageUrl,
    string webBaseUrl, DateTimeOffset createdAt)
{
    var total = hakli + haksiz;
    var hakliPct = total > 0 ? (int)Math.Round(hakli * 100.0 / total) : 0;

    var description = total >= 10
        ? $"{total} kiÅŸi oyladÄ± Â· %{hakliPct} HaklÄ± Â· Sen de karar ver"
        : TruncateForMeta(content, 160);

    var postUrl = $"{webBaseUrl}/posts/{slug}";
    var imageUrl = customImageUrl ?? $"{webBaseUrl}/api/v1/posts/{id}/share-image.png";

    var titleEncoded = WebUtility.HtmlEncode(title);
    var descEncoded = WebUtility.HtmlEncode(description);
    var imageEncoded = WebUtility.HtmlEncode(imageUrl);
    var urlEncoded = WebUtility.HtmlEncode(postUrl);
    var jsonTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
    var dateStr = createdAt.ToString("yyyy-MM-dd");
    var jsonLd = $"{{\"@context\":\"https://schema.org\",\"@type\":\"Question\",\"name\":\"{jsonTitle}\",\"answerCount\":{total},\"datePublished\":\"{dateStr}\",\"author\":{{\"@type\":\"Person\",\"name\":\"Anonim\"}}}}";

    return $$"""
        <!DOCTYPE html>
        <html lang="tr">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>{{titleEncoded}} - Karar</title>
          <meta name="description" content="{{descEncoded}}">
          <link rel="canonical" href="{{urlEncoded}}">
          <meta property="og:type" content="article">
          <meta property="og:title" content="{{titleEncoded}} - Karar">
          <meta property="og:description" content="{{descEncoded}}">
          <meta property="og:image" content="{{imageEncoded}}">
          <meta property="og:url" content="{{urlEncoded}}">
          <meta property="og:locale" content="tr_TR">
          <meta name="twitter:card" content="summary_large_image">
          <meta name="twitter:title" content="{{titleEncoded}} - Karar">
          <meta name="twitter:description" content="{{descEncoded}}">
          <meta name="twitter:image" content="{{imageEncoded}}">
          <script type="application/ld+json">
          {{jsonLd}}
          </script>
          <meta http-equiv="refresh" content="0;url={{urlEncoded}}">
        </head>
        <body></body>
        </html>
        """;
}

static string TruncateForMeta(string value, int maxLength)
{
    var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    if (normalized.Length <= maxLength) return normalized;
    return normalized[..maxLength].TrimEnd() + "...";
}

static IResult? RequireAdmin(HttpRequest request, AdminAuthService adminAuth) =>
    adminAuth.TryGetAdminEmail(request) is null ? Unauthorized() : null;

static (DateTimeOffset From, DateTimeOffset To, DateTimeOffset PreviousFrom, DateTimeOffset PreviousTo, string GroupBy, string Step)? NormalizeAnalyticsRange(
    DateTimeOffset? from,
    DateTimeOffset? to,
    string? groupBy)
{
    var normalizedGroupBy = string.IsNullOrWhiteSpace(groupBy)
        ? "day"
        : groupBy.Trim().ToLowerInvariant();
    var step = normalizedGroupBy switch
    {
        "hour" => "1 hour",
        "day" => "1 day",
        "week" => "1 week",
        _ => null,
    };
    if (step is null)
        return null;

    var end = to?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    var start = from?.ToUniversalTime() ?? end.AddDays(-7);
    if (start >= end)
        return null;

    var maxWindow = TimeSpan.FromDays(90);
    if (end - start > maxWindow)
        start = end - maxWindow;

    var duration = end - start;
    return (start, end, start - duration, start, normalizedGroupBy, step);
}

static void AddAnalyticsParameters(
    NpgsqlCommand command,
    DateTimeOffset from,
    DateTimeOffset to,
    string? platform,
    string? userType,
    string? source,
    int? categoryId)
{
    command.Parameters.Add("from", NpgsqlDbType.TimestampTz).Value = from;
    command.Parameters.Add("to", NpgsqlDbType.TimestampTz).Value = to;
    command.Parameters.Add("platform", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(platform) ? DBNull.Value : platform;
    command.Parameters.Add("userType", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(userType) ? DBNull.Value : userType;
    command.Parameters.Add("source", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(source) ? DBNull.Value : source;
    command.Parameters.Add("categoryId", NpgsqlDbType.Integer).Value = categoryId.HasValue ? categoryId.Value : DBNull.Value;
}

static string BuildDictionaryCsv(IReadOnlyList<Dictionary<string, object?>> rows)
{
    if (rows.Count == 0)
        return "metric,value\n";

    var columns = rows.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).ToArray();
    var csv = new StringBuilder();
    csv.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
    foreach (var row in rows)
    {
        csv.AppendLine(string.Join(",", columns.Select(column =>
            EscapeCsv(row.TryGetValue(column, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : ""))));
    }
    return csv.ToString();
}

static string EscapeCsv(string? value)
{
    var text = value ?? "";
    return text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r')
        ? $"\"{text.Replace("\"", "\"\"")}\""
        : text;
}

// OTP cache: {unix_ts}|{sha256_hash}|{plain_otp}  — TTL = validFor, resend cooldown = resendAfter
// Returns (otp: string, tooSoon: bool, waitSeconds: int)
static async Task<(string? Otp, bool TooSoon, int WaitSeconds, bool IsNew)> GetOrCreateCachedOtpAsync(
    IDatabase redis, string key, TimeSpan validFor, TimeSpan resendAfter)
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var existing = (string?)(await redis.StringGetAsync(key));
    if (existing is not null)
    {
        var parts = existing.Split('|');
        if (parts.Length == 3 && long.TryParse(parts[0], out var ts))
        {
            var age = now - ts;
            var cooldown = (long)resendAfter.TotalSeconds;
            if (age < cooldown)
                return (null, true, (int)(cooldown - age), false);
            // Within validity window — resend same code, do NOT extend TTL
            return (parts[2], false, 0, false);
        }
    }
    var otp = PasswordService.GenerateOtp();
    await redis.StringSetAsync(key, $"{now}|{PasswordService.HashOtp(otp)}|{otp}", validFor);
    return (otp, false, 0, true);
}

static async Task<bool> ValidateCachedOtpAsync(IDatabase redis, string key, string inputOtp)
{
    var existing = (string?)(await redis.StringGetAsync(key));
    if (existing is null) return false;
    var parts = existing.Split('|');
    return parts.Length == 3 && parts[1] == PasswordService.HashOtp(inputOtp);
}

static IResult? ValidateRequest(object request)
{
    var results = new List<ValidationResult>();
    var context = new ValidationContext(request);
    if (Validator.TryValidateObject(request, context, results, validateAllProperties: true))
    {
        return null;
    }

    var message = results.FirstOrDefault()?.ErrorMessage ?? "Ä°stek doÄŸrulamasÄ± baÅŸarÄ±sÄ±z.";
    return BadRequest("VALIDATION_ERROR", message);
}

// â"€â"€ Karma + Verdict Milestone â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

// Post oy sayÄ±sÄ± milestone'larÄ±nÄ± kontrol eder ve karma gÃ¼nceller.
// oldTotal â†' newTotal geÃ§iÅŸinde hangi eÅŸikler aÅŸÄ±ldÄ±ysa tek seferlik karma verir.
static async Task CheckPostVoteMilestoneAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    int oldTotal,
    int newTotal
)
{
    // post sahibi user_id ve device_id'yi Ã§ek
    await using var ownerCmd = new NpgsqlCommand(
        "SELECT user_id, device_id FROM posts WHERE id = @postId",
        connection,
        transaction
    );
    ownerCmd.Parameters.AddWithValue("postId", postId);
    await using var ownerReader = await ownerCmd.ExecuteReaderAsync();
    if (!await ownerReader.ReadAsync()) return;
    var userId = ownerReader.IsDBNull(0) ? (Guid?)null : ownerReader.GetGuid(0);
    var ownerDeviceId = ownerReader.GetGuid(1);
    await ownerReader.CloseAsync();

    // Milestone tablosu: eÅŸik â†' karma_delta
    var milestones = new (int Threshold, int Delta, string NotifBody)[]
    {
        (10, 1, "İlk tepkiler geliyor. %{pct} Haklı buluyor."),
        (25, 2, "%{pct} topluluğun kararı şekilleniyor."),
        (50, 3, "Karar netleşiyor. Şu an %{pct} Haklı."),
        (100, 5, "100 kişi yargıladı. %{pct} Haklı buluyor."),
        (250, 10, "Postun büyük dikkat çekiyor. %{pct} Haklı buluyor."),
    };

    // Oy oranÄ±nÄ± hesapla (notification mesajÄ± iÃ§in)
    await using var votesCmd = new NpgsqlCommand(
        "SELECT vote_count_hakli, vote_count_haksiz FROM posts WHERE id = @postId",
        connection,
        transaction
    );
    votesCmd.Parameters.AddWithValue("postId", postId);
    await using var votesReader = await votesCmd.ExecuteReaderAsync();
    int hakliPct = 0;
    if (await votesReader.ReadAsync() && !votesReader.IsDBNull(0))
    {
        var hakli = votesReader.GetInt32(0);
        var haksiz = votesReader.GetInt32(1);
        var total = hakli + haksiz;
        hakliPct = total > 0 ? (int)Math.Round(hakli * 100.0 / total) : 0;
    }
    await votesReader.CloseAsync();

    foreach (var (threshold, delta, notifTemplate) in milestones)
    {
        if (oldTotal < threshold && newTotal >= threshold)
        {
            // Karma gÃ¼ncelle (sadece kayÄ±tlÄ± kullanÄ±cÄ± ise)
            if (userId.HasValue)
            {
                await using var karmaCmd = new NpgsqlCommand(
                    """
                    INSERT INTO karma_milestones (user_id, source_type, source_id, milestone, karma_delta)
                    VALUES (@userId, 'post_vote', @postId, @milestone, @delta)
                    ON CONFLICT (source_type, source_id, milestone) DO NOTHING
                    """,
                    connection,
                    transaction
                );
                karmaCmd.Parameters.AddWithValue("userId", userId.Value);
                karmaCmd.Parameters.AddWithValue("postId", postId);
                karmaCmd.Parameters.AddWithValue("milestone", threshold);
                karmaCmd.Parameters.AddWithValue("delta", delta);
                var inserted = await karmaCmd.ExecuteNonQueryAsync();

                if (inserted > 0)
                {
                    await using var updateKarma = new NpgsqlCommand(
                        "UPDATE users SET karma = GREATEST(-100, karma + @delta) WHERE id = @userId",
                        connection,
                        transaction
                    );
                    updateKarma.Parameters.AddWithValue("delta", delta);
                    updateKarma.Parameters.AddWithValue("userId", userId.Value);
                    await updateKarma.ExecuteNonQueryAsync();
                }
            }

            // Verdict milestone bildirimi (cihaz bazlÄ± â€" kayÄ±tlÄ± veya misafir)
            var body = notifTemplate.Replace("%{pct}", $"%{hakliPct}");
            await using var notifCmd = new NpgsqlCommand(
                """
                INSERT INTO notifications (device_id, type, title, body, post_id)
                VALUES (@deviceId, 'verdict_milestone', 'Topluluk karar verdi', @body, @postId)
                """,
                connection,
                transaction
            );
            notifCmd.Parameters.AddWithValue("deviceId", ownerDeviceId);
            notifCmd.Parameters.AddWithValue("body", body);
            notifCmd.Parameters.AddWithValue("postId", postId);
            await notifCmd.ExecuteNonQueryAsync();
        }
    }
}

// Comment upvote milestone'larÄ±nÄ± kontrol eder.
static async Task CheckCommentUpvoteMilestoneAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid commentId,
    int oldUpvotes,
    int newUpvotes
)
{
    await using var ownerCmd = new NpgsqlCommand(
        "SELECT user_id FROM comments WHERE id = @commentId",
        connection,
        transaction
    );
    ownerCmd.Parameters.AddWithValue("commentId", commentId);
    var result = await ownerCmd.ExecuteScalarAsync();
    if (result is not Guid userId) return;

    var milestones = new (int Threshold, int Delta)[] { (5, 2), (20, 5) };

    foreach (var (threshold, delta) in milestones)
    {
        if (oldUpvotes < threshold && newUpvotes >= threshold)
        {
            await using var karmaCmd = new NpgsqlCommand(
                """
                INSERT INTO karma_milestones (user_id, source_type, source_id, milestone, karma_delta)
                VALUES (@userId, 'comment_upvote', @commentId, @milestone, @delta)
                ON CONFLICT (source_type, source_id, milestone) DO NOTHING
                """,
                connection,
                transaction
            );
            karmaCmd.Parameters.AddWithValue("userId", userId);
            karmaCmd.Parameters.AddWithValue("commentId", commentId);
            karmaCmd.Parameters.AddWithValue("milestone", threshold);
            karmaCmd.Parameters.AddWithValue("delta", delta);
            var inserted = await karmaCmd.ExecuteNonQueryAsync();

            if (inserted > 0)
            {
                await using var updateKarma = new NpgsqlCommand(
                    "UPDATE users SET karma = GREATEST(-100, karma + @delta) WHERE id = @userId",
                    connection,
                    transaction
                );
                updateKarma.Parameters.AddWithValue("delta", delta);
                updateKarma.Parameters.AddWithValue("userId", userId);
                await updateKarma.ExecuteNonQueryAsync();
            }
        }
    }
}

static async Task<int> UpdateModerationTargetAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string targetType,
    Guid targetId,
    string status
)
{
    var table = targetType == "post" ? "posts" : "comments";
    await using var command = new NpgsqlCommand(
        $"""
        UPDATE {table}
        SET status = @status,
            updated_at = NOW()
        WHERE id = @targetId
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("targetId", targetId);
    command.Parameters.AddWithValue("status", status);
    return await command.ExecuteNonQueryAsync();
}

static async Task MarkReportsForTargetAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string targetType,
    Guid targetId,
    string status
)
{
    await using var command = new NpgsqlCommand(
        """
        UPDATE reports
        SET status = @status
        WHERE target_type = @targetType AND target_id = @targetId AND status = 'pending'
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("targetType", targetType);
    command.Parameters.AddWithValue("targetId", targetId);
    command.Parameters.AddWithValue("status", status);
    await command.ExecuteNonQueryAsync();
}

static async Task InsertCrisisSupportNotificationForContentAsync(
    NpgsqlConnection connection,
    Guid deviceId,
    Guid postId)
{
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body, post_id, payload)
        VALUES (
            @deviceId,
            'crisis_support',
            'Destek alabilirsin',
            'Zor bir an yaşıyorsan yalnız değilsin. 182 hattını arayabilir veya imece.org üzerinden destek kaynaklarına ulaşabilirsin.',
            @postId,
            @payload::jsonb
        )
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId);
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new
    {
        support_phone = "182",
        support_url = "https://www.imece.org"
    }));
    await command.ExecuteNonQueryAsync();
}

// İlk 24 saatte ≥10 rapor alan cihazı bayrakla (fire-and-forget)
static async Task CheckAndFlagNewAccountHighReportAsync(Db db, string targetType, Guid targetId)
{
    try
    {
        await using var connection = await db.OpenConnectionAsync();

        var table = targetType == "post" ? "posts" : "comments";
        await using var deviceCmd = new NpgsqlCommand(
            $"""
            SELECT d.id, d.created_at,
                   (SELECT COUNT(*) FROM reports r2
                    JOIN {table} t2 ON t2.id = r2.target_id AND r2.target_type = @targetType
                    WHERE t2.device_id = d.id) AS report_count
            FROM {table} t
            JOIN devices d ON d.id = t.device_id
            WHERE t.id = @targetId
              AND d.created_at >= NOW() - INTERVAL '24 hours'
            """,
            connection
        );
        deviceCmd.Parameters.AddWithValue("targetId", targetId);
        deviceCmd.Parameters.AddWithValue("targetType", targetType);

        await using var reader = await deviceCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return;

        var contentDeviceId = reader.GetGuid(0);
        var reportCount = Convert.ToInt32(reader.GetInt64(2));
        await reader.CloseAsync();

        if (reportCount < 10) return;

        // Flag device
        await using var flagCmd = new NpgsqlCommand(
            """
            UPDATE devices
            SET flags = flags || '{"new_account_high_report": true}'::jsonb
            WHERE id = @deviceId
              AND NOT (flags ? 'new_account_high_report')
            """,
            connection
        );
        flagCmd.Parameters.AddWithValue("deviceId", contentDeviceId);
        var updated = await flagCmd.ExecuteNonQueryAsync();

        if (updated > 0)
        {
            await using var alertCmd = new NpgsqlCommand(
                """
                INSERT INTO admin_alerts (type, payload)
                VALUES ('new_account_high_report',
                        jsonb_build_object(
                            'device_id', @deviceId::text,
                            'report_count', @reportCount,
                            'detected_at', NOW()
                        ))
                """,
                connection
            );
            alertCmd.Parameters.AddWithValue("deviceId", contentDeviceId);
            alertCmd.Parameters.AddWithValue("reportCount", reportCount);
            await alertCmd.ExecuteNonQueryAsync();
        }
    }
    catch { }
}

static async Task LogAdminActionAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string adminEmail,
    string action,
    string targetType,
    Guid? targetId,
    string? note
)
{
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO admin_actions (admin_email, action, target_type, target_id, note)
        VALUES (@adminEmail, @action, @targetType, @targetId, @note)
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("adminEmail", adminEmail);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("targetType", targetType);
    command.Parameters.AddWithValue("targetId", (object?)targetId ?? DBNull.Value);
    command.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}

static async Task<bool> ReportTargetExistsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string targetType,
    Guid targetId
)
{
    var table = targetType == "post" ? "posts" : "comments";
    await using var command = new NpgsqlCommand(
        $"""
        SELECT EXISTS (
            SELECT 1
            FROM {table}
            WHERE id = @targetId AND status IN ('active', 'under_review', 'auto_hidden')
        )
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("targetId", targetId);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<ReportThresholdDecision> EvaluateReportThresholdAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    ReportThresholdService reportThresholdService,
    string targetType,
    Guid targetId
)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT d.fingerprint,
               r.reporter_ip_block,
               r.reason,
               COALESCE(u.reporter_accurate_count, 0),
               COALESCE(u.reporter_total_count,   0)
        FROM reports r
        JOIN devices d ON d.id = r.reporter_device_id
        LEFT JOIN users u ON u.id = r.reporter_user_id
        WHERE r.target_type = @targetType
          AND r.target_id = @targetId
          AND r.status = 'pending'
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("targetType", targetType);
    command.Parameters.AddWithValue("targetId", targetId);

    // Raw rows: (fingerprint, ipBlock, reason, accurateCount, totalCount)
    var rows = new List<(string Fingerprint, string? IpBlock, string Reason, int Accurate, int Total)>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4)
            ));
        }
    }

    // Compute effective weight per reporter: fingerprint/IP uniqueness × reputation.
    // Each reporter is compared against all other reporters (not against itself) so the
    // first reporter from a given subnet is penalised only when there are other same-subnet
    // reporters in the batch — i.e. when a coordinated cluster is actually present.
    var allSignals = rows.Select(r => (r.Fingerprint, r.IpBlock)).ToList();
    var reports = rows.Select((row, i) =>
    {
        var others = allSignals.Where((_, j) => j != i);
        var ipFpWeight = ReportAbuseProtectionService.ComputeReportWeight(row.Fingerprint, row.IpBlock, others);
        var reputationWeight = ReporterReputationService.ComputeWeight(row.Accurate, row.Total);
        return new ReportSignal(row.Fingerprint, row.IpBlock, row.Reason, ipFpWeight * reputationWeight);
    }).ToList();

    var weightedReporters = reports.Sum(r => r.Weight);
    var weightedCritical  = reports
        .Where(r => reportThresholdService.IsCriticalReason(r.Reason))
        .Sum(r => r.Weight);

    return reportThresholdService.Evaluate(targetType, weightedReporters, weightedCritical);
}

static string? GetClientIpBlock(HttpRequest request)
{
    return IpAddressPrivacy.ToNetworkBlock(request.HttpContext.Connection.RemoteIpAddress);
}

static async Task AutoHideReportedTargetAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string targetType,
    Guid targetId,
    string reason
)
{
    var table = targetType == "post" ? "posts" : "comments";
    await using var command = new NpgsqlCommand(
        $"""
        UPDATE {table}
        SET status = 'under_review',
            moderation_reason = @reason,
            moderation_checked_at = NOW(),
            updated_at = NOW()
        WHERE id = @targetId AND status != 'deleted'
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("targetId", targetId);
    command.Parameters.AddWithValue("reason", reason);
    await command.ExecuteNonQueryAsync();
}

static IReadOnlyList<PostDto> LabelPosts(IReadOnlyList<PostDto> posts, string reason, string label)
{
    if (posts.Count == 0) return posts;
    return posts.Select(post => post with { RankingReason = reason, RankingLabel = label }).ToList();
}

static string EncodeCursor(double trendScore, Guid id) =>
    Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(
            $"{trendScore.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}|{id}"));

static (double TrendScore, Guid Id)? DecodeCursor(string? cursor)
{
    if (string.IsNullOrWhiteSpace(cursor)) return null;
    try
    {
        var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = raw.Split('|');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var score)) return null;
        if (!Guid.TryParse(parts[1], out var id)) return null;
        return (score, id);
    }
    catch { return null; }
}

static async Task<IReadOnlyList<PostDto>> ReadPostsAsync(NpgsqlCommand command)
{
    var posts = new List<PostDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        // Column layout varies by query:
        // 14 cols (0-13): minimal "my posts" query — col13=isOwner (TRUE literal)
        // 16 cols (0-15): standard feed/discover/saved — col13=isOwner, col14=isUnlisted, col15=tags
        // 17 cols (0-16): post detail — col14=isUnlisted, col15=tags, col16=ai_summary
        // 18 cols (0-17): other-user profile query — col13=isOwner, col14=isEdited, col15=isSaved, col16=authorName, col17=tags

        var fc = reader.FieldCount;
        var isOwner = fc > 13 && !reader.IsDBNull(13) && reader.GetFieldType(13) == typeof(bool) && reader.GetBoolean(13);
        var isEdited = ReadBool("is_edited") ?? (fc >= 18 && !reader.IsDBNull(14) && reader.GetFieldType(14) == typeof(bool) && reader.GetBoolean(14));
        var isSaved = ReadBool("is_saved") ?? (fc >= 18 && !reader.IsDBNull(15) && reader.GetFieldType(15) == typeof(bool) && reader.GetBoolean(15));
        var isUnlisted = ReadBool("is_unlisted") ?? false;
        var isAnonymous = ReadBool("is_anonymous") ?? false;
        string? authorName = ReadString("author_name") ?? ReadString("username");
        Guid? authorId = ReadGuid("author_id");
        var tags = ReadStringArray("tags");
        var aiSummary = ReadString("ai_summary");
        var contentSource = ReadString("content_source") ?? "user";
        var status = ReadString("status");
        var moderationReason = ReadString("moderation_reason");

        // Redact author if anonymous
        if (isAnonymous) {
            authorName = null;
            authorId = null;
        }

        var createdAt = reader.GetFieldValue<DateTimeOffset>(12);
        var isClosed = createdAt.AddDays(7) <= DateTimeOffset.UtcNow;
        posts.Add(new PostDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            new CategoryDto(reader.GetInt32(4), reader.GetString(5), reader.GetString(6)),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetDouble(11),
            createdAt,
            isOwner,
            Status: status,
            ModerationReason: moderationReason,
            IsEdited: isEdited,
            IsSaved: isSaved,
            AuthorName: authorName,
            AuthorId: authorId,
            IsUnlisted: isUnlisted,
            IsAnonymous: isAnonymous,
            IsClosed: isClosed,
            Tags: tags,
            AiSummary: aiSummary,
            ContentSource: contentSource,
            RankingAuthorKey: ReadGuid("ranking_author_key")
        ));

        bool? ReadBool(string name)
        {
            var idx = GetOrdinalOrMinusOne(name);
            return idx == -1 || reader.IsDBNull(idx) ? null : reader.GetBoolean(idx);
        }

        string? ReadString(string name)
        {
            var idx = GetOrdinalOrMinusOne(name);
            return idx == -1 || reader.IsDBNull(idx) ? null : reader.GetString(idx);
        }

        Guid? ReadGuid(string name)
        {
            var idx = GetOrdinalOrMinusOne(name);
            return idx == -1 || reader.IsDBNull(idx) ? null : reader.GetGuid(idx);
        }

        IReadOnlyList<string>? ReadStringArray(string name)
        {
            var idx = GetOrdinalOrMinusOne(name);
            return idx == -1 || reader.IsDBNull(idx) ? null : reader.GetFieldValue<string[]>(idx);
        }

        int GetOrdinalOrMinusOne(string name)
        {
            try { return reader.GetOrdinal(name); } catch { return -1; }
        }
    }
    return posts;
}

static async Task<(IReadOnlyList<PostDto> Posts, int Total)> ReadPostsWithTotalAsync(NpgsqlCommand command)
{
    // Search query layout: 0-12 standard, 13=isOwner, 14=total_count, 15=rank, 16=tags
    var posts = new List<PostDto>();
    var total = 0;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var isOwner = reader.FieldCount > 13 && reader.GetBoolean(13);
        var tags = reader.FieldCount > 16 && !reader.IsDBNull(16)
            ? reader.GetFieldValue<string[]>(16)
            : null;

        int anonIdx = -1;
        try { anonIdx = reader.GetOrdinal("is_anonymous"); } catch {}
        bool isAnonymous = anonIdx != -1 && reader.GetBoolean(anonIdx);

        var createdAt = reader.GetFieldValue<DateTimeOffset>(12);
        var isClosed = createdAt.AddDays(7) <= DateTimeOffset.UtcNow;
        posts.Add(new PostDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            new CategoryDto(reader.GetInt32(4), reader.GetString(5), reader.GetString(6)),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetDouble(11),
            createdAt,
            isOwner,
            IsAnonymous: isAnonymous,
            IsClosed: isClosed,
            Tags: tags
        ));
        if (posts.Count == 1)
            total = Convert.ToInt32(reader.GetInt64(14));
    }

    return (posts, total);
}

static async Task<string?> GetExistingVoteAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    Guid deviceId
)
{
    await using var command = new NpgsqlCommand(
        "SELECT vote_type FROM votes WHERE post_id = @postId AND device_id = @deviceId",
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    return await command.ExecuteScalarAsync() as string;
}

static Task AddVoteTimingJitterAsync(CancellationToken cancellationToken = default)
{
    return Task.Delay(Random.Shared.Next(5, 51), cancellationToken);
}

static Task EnforceMinimumResponseTimeAsync(
    System.Diagnostics.Stopwatch stopwatch,
    CancellationToken cancellationToken = default
)
{
    var remaining = TimeSpan.FromMilliseconds(200) - stopwatch.Elapsed;
    return remaining <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(remaining, cancellationToken);
}

static async Task<(string? OldVote, int OldTotal, int Hakli, int Haksiz, int CategoryId, DateTimeOffset CreatedAt)> GetVoteContextAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    Guid deviceId
)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT p.vote_count_hakli, p.vote_count_haksiz, v.vote_type, p.category_id, p.created_at
        FROM posts p
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = @postId
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return (null, 0, 0, 0, 0, DateTimeOffset.MinValue);
    var hakli = reader.GetInt32(0);
    var haksiz = reader.GetInt32(1);
    var oldVote = reader.IsDBNull(2) ? null : reader.GetString(2);
    var categoryId = reader.GetInt32(3);
    var createdAt = reader.GetFieldValue<DateTimeOffset>(4);
    return (oldVote, hakli + haksiz, hakli, haksiz, categoryId, createdAt);
}

static async Task<(int Hakli, int Haksiz)> UpdateVoteCountersReturningAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    string? oldVote,
    string? newVote
)
{
    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET vote_count_hakli = (
                SELECT COUNT(*)::int
                FROM votes v
                WHERE v.post_id = posts.id
                  AND v.vote_type = 'hakli'
                  AND v.is_suppressed = FALSE
            ),
            vote_count_haksiz = (
                SELECT COUNT(*)::int
                FROM votes v
                WHERE v.post_id = posts.id
                  AND v.vote_type = 'haksiz'
                  AND v.is_suppressed = FALSE
            ),
            trend_score = (
                (
                    SELECT COUNT(*)
                    FROM votes v
                    WHERE v.post_id = posts.id
                      AND v.is_quarantined = FALSE
                      AND v.is_suppressed = FALSE
                ) + (comment_count * 3)
            )
                / POWER(EXTRACT(EPOCH FROM (NOW() - created_at)) / 3600 + 2, 1.5),
            updated_at = NOW()
        WHERE id = @postId
        RETURNING vote_count_hakli, vote_count_haksiz
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("Post not found after vote mutation.");
    return (reader.GetInt32(0), reader.GetInt32(1));
}

static async Task UpsertVoteAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    Guid deviceId,
    string voteType,
    string? voterIpBlock = null,
    bool isQuarantined = false,
    string? voterRegion = null
)
{
    await using var command = new NpgsqlCommand(
        """
        INSERT INTO votes (post_id, device_id, vote_type, voter_ip_block, is_quarantined, voter_region, is_suppressed)
        VALUES (@postId, @deviceId, @voteType, @ipBlock, @isQuarantined, @voterRegion, FALSE)
        ON CONFLICT (post_id, device_id)
        DO UPDATE SET
            vote_type = @voteType,
            voter_ip_block = COALESCE(@ipBlock, votes.voter_ip_block),
            voter_region = COALESCE(@voterRegion, votes.voter_region),
            is_quarantined = @isQuarantined,
            is_suppressed = FALSE,
            suppression_reason = NULL,
            suppressed_at = NULL,
            updated_at = NOW()
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    command.Parameters.AddWithValue("voteType", voteType);
    command.Parameters.AddWithValue("ipBlock", (object?)voterIpBlock ?? DBNull.Value);
    command.Parameters.AddWithValue("isQuarantined", isQuarantined);
    command.Parameters.AddWithValue("voterRegion", (object?)voterRegion ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}

static async Task UpdateVoteCountersAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    string? oldVote,
    string? newVote
)
{
    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET vote_count_hakli = (
                SELECT COUNT(*)::int
                FROM votes v
                WHERE v.post_id = posts.id
                  AND v.vote_type = 'hakli'
                  AND v.is_suppressed = FALSE
            ),
            vote_count_haksiz = (
                SELECT COUNT(*)::int
                FROM votes v
                WHERE v.post_id = posts.id
                  AND v.vote_type = 'haksiz'
                  AND v.is_suppressed = FALSE
            ),
            trend_score = (
                (
                    SELECT COUNT(*)
                    FROM votes v
                    WHERE v.post_id = posts.id
                      AND v.is_quarantined = FALSE
                      AND v.is_suppressed = FALSE
                ) + (comment_count * 3)
            )
                / POWER(EXTRACT(EPOCH FROM (NOW() - created_at)) / 3600 + 2, 1.5),
            updated_at = NOW()
        WHERE id = @postId
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    await command.ExecuteNonQueryAsync();
}

static async Task<VoteResponse> GetVoteResponseAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    string? myVote
)
{
    await using var command = new NpgsqlCommand(
        "SELECT vote_count_hakli, vote_count_haksiz FROM posts WHERE id = @postId",
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Post not found after vote mutation.");
    }

    return new VoteResponse(reader.GetInt32(0), reader.GetInt32(1), myVote);
}

static string GenerateBase32Secret(int byteLength = 20)
{
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    Span<byte> bytes = stackalloc byte[byteLength];
    System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
    var chars = new char[(int)Math.Ceiling(byteLength * 8 / 5d)];
    var buffer = 0;
    var bitsLeft = 0;
    var index = 0;

    foreach (var b in bytes)
    {
        buffer = (buffer << 8) | b;
        bitsLeft += 8;
        while (bitsLeft >= 5)
        {
            chars[index++] = alphabet[(buffer >> (bitsLeft - 5)) & 31];
            bitsLeft -= 5;
        }
    }

    if (bitsLeft > 0)
    {
        chars[index++] = alphabet[(buffer << (5 - bitsLeft)) & 31];
    }

    return new string(chars, 0, index);
}

static System.Security.Claims.ClaimsPrincipal? GetJwtPrincipal(HttpRequest request, JwtService jwtService)
{
    if (!request.Headers.TryGetValue("Authorization", out var values)) return null;
    var header = values.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    return jwtService.ValidateAccessToken(header["Bearer ".Length..].Trim());
}

static Guid GetUserId(System.Security.Claims.ClaimsPrincipal principal)
{
    var sub = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
           ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return Guid.Parse(sub!);
}

static Guid? GetOptionalUserId(HttpRequest request, JwtService jwtService)
{
    var principal = GetJwtPrincipal(request, jwtService);
    return principal is null ? null : GetUserId(principal);
}

static async Task<Guid?> GetUserDeviceIdAsync(
    NpgsqlConnection connection,
    Guid userId,
    NpgsqlTransaction? transaction = null
)
{
    await using var command = new NpgsqlCommand(
        "SELECT device_id FROM users WHERE id = @userId AND deleted_at IS NULL",
        connection,
        transaction
    );
    command.Parameters.AddWithValue("userId", userId);
    var result = await command.ExecuteScalarAsync();
    return result is Guid deviceId ? deviceId : null;
}

static async Task<bool> IsPostOwnerAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid postId,
    Guid deviceId,
    Guid? userId
)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT EXISTS (
            SELECT 1
            FROM posts
            WHERE id = @postId
              AND status != 'deleted'
              AND (device_id = @deviceId OR user_id = @userId)
        )
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

// Returns true when the post author has blocked actorUserId.
// Only meaningful for registered users — anonymous devices are not in blocked_users.
static async Task<bool> IsBlockedByPostAuthorAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    Guid postId,
    Guid actorUserId
)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT EXISTS (
            SELECT 1
            FROM blocked_users bu
            JOIN posts p ON p.user_id = bu.blocker_user_id
            WHERE p.id = @postId
              AND bu.blocked_user_id = @actorUserId
              AND p.user_id IS NOT NULL
        )
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("postId", postId);
    command.Parameters.AddWithValue("actorUserId", actorUserId);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<(CreatePostRequest? Request, IFormFile? Image)> ReadCreatePostRequestWithImageAsync(HttpRequest request)
{
    if (!request.HasFormContentType)
    {
        var req = await request.ReadFromJsonAsync<CreatePostRequest>();
        return (req, null);
    }

    var form = await request.ReadFormAsync();
    var title = form["title"].ToString();
    var content = form["content"].ToString();
    _ = int.TryParse(form["categoryId"].ToString(), out var categoryId);
    _ = bool.TryParse(form["isUnlisted"].ToString(), out var isUnlisted);
    _ = bool.TryParse(form["isAnonymous"].ToString(), out var isAnonymous);
    _ = bool.TryParse(form["acceptedTerms"].ToString(), out var acceptedTerms);
    _ = bool.TryParse(form["acceptedCommunityGuidelines"].ToString(), out var acceptedCommunityGuidelines);
    var imageFile = form.Files.GetFile("image");

    // Tags: comma-separated in form data (e.g. "haber,spor") or individual values
    IReadOnlyList<string>? tags = null;
    var tagsRaw = form["tags"].ToString();
    if (!string.IsNullOrWhiteSpace(tagsRaw))
    {
        tags = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(t => t.TrimStart('#').ToLowerInvariant())
                      .Where(t => t.Length is >= 1 and <= 32)
                      .Distinct()
                      .Take(3)
                      .ToList();
    }

    return (new CreatePostRequest(
        title,
        content,
        categoryId,
        null,
        isUnlisted,
        isAnonymous,
        tags,
        acceptedTerms,
        acceptedCommunityGuidelines), imageFile);
}

static async Task<DeleteAccountRequest?> ReadOptionalDeleteAccountRequestAsync(HttpRequest request)
{
    if (request.ContentLength is null or 0)
    {
        return null;
    }

    if (!request.HasJsonContentType())
    {
        return null;
    }

    return await request.ReadFromJsonAsync<DeleteAccountRequest>();
}

static async Task<CommentDto?> ReadCommentDtoAsync(
    NpgsqlConnection connection,
    Guid commentId,
    Guid deviceId,
    Guid? userId
)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT cm.id, cm.content, cm.upvote_count, cm.downvote_count, cu.comment_id IS NOT NULL, cd.comment_id IS NOT NULL,
               (cm.device_id = @deviceId OR cm.user_id = @userId), cm.created_at,
               cm.is_pinned, u.username, cm.user_id, cm.is_edited,
               ((cm.user_id IS NOT NULL AND cm.user_id = p.user_id) OR (cm.user_id IS NULL AND cm.device_id = p.device_id)) AS is_post_owner,
               (SELECT COUNT(*)::int FROM comment_upvotes cu_h
                JOIN votes v_h ON v_h.device_id = cu_h.device_id AND v_h.post_id = cm.post_id AND v_h.vote_type = 'hakli'
                WHERE cu_h.comment_id = cm.id) AS upvotes_hakli,
               (SELECT COUNT(*)::int FROM comment_upvotes cu_z
                JOIN votes v_z ON v_z.device_id = cu_z.device_id AND v_z.post_id = cm.post_id AND v_z.vote_type = 'haksiz'
                WHERE cu_z.comment_id = cm.id) AS upvotes_haksiz,
               COALESCE((
                   SELECT jsonb_object_agg(reaction_counts.emoji, reaction_counts.count)
                   FROM (
                       SELECT emoji, COUNT(*)::int AS count
                       FROM comment_reactions
                       WHERE comment_id = cm.id
                       GROUP BY emoji
                   ) reaction_counts
               ), '{}'::jsonb)::text AS reactions_json,
               (SELECT emoji FROM comment_reactions WHERE comment_id = cm.id AND device_id = @deviceId) AS my_reaction,
               cm.parent_id
        FROM comments cm
        JOIN posts p ON p.id = cm.post_id
        LEFT JOIN comment_upvotes cu ON cu.comment_id = cm.id AND cu.device_id = @deviceId
        LEFT JOIN comment_downvotes cd ON cd.comment_id = cm.id AND cd.device_id = @deviceId
        LEFT JOIN users u ON u.id = cm.user_id
        WHERE cm.id = @commentId AND cm.status = 'active'
        """,
        connection
    );
    command.Parameters.AddWithValue("commentId", commentId);
    command.Parameters.AddWithValue("deviceId", deviceId);
    command.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;

    return new CommentDto(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.GetBoolean(6),
        reader.GetFieldValue<DateTimeOffset>(7),
        IsPinned: reader.GetBoolean(8),
        AuthorName: reader.IsDBNull(9) ? null : reader.GetString(9),
        AuthorId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
        IsEdited: reader.GetBoolean(11),
        IsPostOwner: reader.GetBoolean(12),
        UpvotesHakli: reader.GetInt32(13),
        UpvotesHaksiz: reader.GetInt32(14),
        Reactions: JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(15)) ?? new Dictionary<string, int>(),
        MyReaction: reader.IsDBNull(16) ? null : reader.GetString(16),
        ParentId: reader.IsDBNull(17) ? null : reader.GetGuid(17)
    );
}

static async Task<IResult> SetCommentUpvoteAsync(
    Guid commentId,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    RedisService redis,
    bool shouldUpvote
)
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var newUpvote = false;
    if (shouldUpvote)
    {
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO comment_upvotes (comment_id, device_id)
            VALUES (@commentId, @deviceId)
            ON CONFLICT DO NOTHING
            """,
            connection,
            transaction
        );
        insert.Parameters.AddWithValue("commentId", commentId);
        insert.Parameters.AddWithValue("deviceId", deviceId.Value);
        var inserted = await insert.ExecuteNonQueryAsync();
        if (inserted == 1)
        {
            newUpvote = true;
            await AddCommentUpvoteDeltaAsync(connection, transaction, commentId, 1);
        }
    }
    else
    {
        await using var delete = new NpgsqlCommand(
            "DELETE FROM comment_upvotes WHERE comment_id = @commentId AND device_id = @deviceId",
            connection,
            transaction
        );
        delete.Parameters.AddWithValue("commentId", commentId);
        delete.Parameters.AddWithValue("deviceId", deviceId.Value);
        var deleted = await delete.ExecuteNonQueryAsync();
        if (deleted == 1)
        {
            await AddCommentUpvoteDeltaAsync(connection, transaction, commentId, -1);
        }
    }

    await using var get = new NpgsqlCommand(
        "SELECT c.upvote_count, c.post_id FROM comments c WHERE c.id = @commentId",
        connection,
        transaction
    );
    get.Parameters.AddWithValue("commentId", commentId);
    await using var getReader = await get.ExecuteReaderAsync();
    var upvoteCount = 0;
    var postId = Guid.Empty;
    if (await getReader.ReadAsync())
    {
        upvoteCount = getReader.GetInt32(0);
        postId = getReader.GetGuid(1);
    }
    await getReader.CloseAsync();

    if (shouldUpvote)
    {
        await CheckCommentUpvoteMilestoneAsync(connection, transaction, commentId, upvoteCount - 1, upvoteCount);
    }

    await transaction.CommitAsync();

    // Track rising comment velocity in Redis (fire-and-forget)
    if (newUpvote && postId != Guid.Empty)
    {
        _ = redis.IncrementRisingCommentAsync(postId, commentId);
    }

    return Results.Ok(new UpvoteResponse(upvoteCount, shouldUpvote));
}

static async Task<IResult> SetCommentDownvoteAsync(
    Guid commentId,
    HttpRequest httpRequest,
    Db db,
    RequestDevice requestDevice,
    bool shouldDownvote
)
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    if (shouldDownvote)
    {
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO comment_downvotes (comment_id, device_id)
            VALUES (@commentId, @deviceId)
            ON CONFLICT DO NOTHING
            """,
            connection,
            transaction
        );
        insert.Parameters.AddWithValue("commentId", commentId);
        insert.Parameters.AddWithValue("deviceId", deviceId.Value);
        var inserted = await insert.ExecuteNonQueryAsync();
        if (inserted == 1)
        {
            await using var upd = new NpgsqlCommand(
                "UPDATE comments SET downvote_count = GREATEST(0, downvote_count + 1), updated_at = NOW() WHERE id = @commentId",
                connection, transaction
            );
            upd.Parameters.AddWithValue("commentId", commentId);
            await upd.ExecuteNonQueryAsync();
        }
    }
    else
    {
        await using var delete = new NpgsqlCommand(
            "DELETE FROM comment_downvotes WHERE comment_id = @commentId AND device_id = @deviceId",
            connection, transaction
        );
        delete.Parameters.AddWithValue("commentId", commentId);
        delete.Parameters.AddWithValue("deviceId", deviceId.Value);
        var deleted = await delete.ExecuteNonQueryAsync();
        if (deleted == 1)
        {
            await using var upd = new NpgsqlCommand(
                "UPDATE comments SET downvote_count = GREATEST(0, downvote_count - 1), updated_at = NOW() WHERE id = @commentId",
                connection, transaction
            );
            upd.Parameters.AddWithValue("commentId", commentId);
            await upd.ExecuteNonQueryAsync();
        }
    }

    await using var get = new NpgsqlCommand(
        "SELECT downvote_count FROM comments WHERE id = @commentId",
        connection, transaction
    );
    get.Parameters.AddWithValue("commentId", commentId);
    var downvoteCount = Convert.ToInt32(await get.ExecuteScalarAsync());

    await transaction.CommitAsync();
    return Results.Ok(new { downvoteCount, myDownvote = shouldDownvote });
}

static async Task AddCommentUpvoteDeltaAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid commentId,
    int delta
)
{
    await using var command = new NpgsqlCommand(
        """
        UPDATE comments
        SET upvote_count = GREATEST(0, upvote_count + @delta),
            updated_at = NOW()
        WHERE id = @commentId
        """,
        connection,
        transaction
    );
    command.Parameters.AddWithValue("commentId", commentId);
    command.Parameters.AddWithValue("delta", delta);
    await command.ExecuteNonQueryAsync();
}

static AdminDeviceDto ReadAdminDevice(NpgsqlDataReader reader) => new(
    reader.GetGuid(0),
    reader.GetString(1),
    reader.GetString(2),
    reader.GetBoolean(3),
    reader.GetFieldValue<DateTimeOffset>(4),
    reader.GetFieldValue<DateTimeOffset>(5),
    Convert.ToInt32(reader.GetInt64(6)),
    Convert.ToInt32(reader.GetInt64(7)),
    Convert.ToInt32(reader.GetInt64(8)),
    reader.IsDBNull(9) ? null : reader.GetString(9),
    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)
);

static void AddAdminCommentFilters(NpgsqlCommand command, Guid? postId, string status, string search)
{
    if (postId is not null)
    {
        command.Parameters.AddWithValue("postId", postId.Value);
    }

    if (status != "all")
    {
        command.Parameters.AddWithValue("status", status);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        command.Parameters.AddWithValue("search", $"%{search}%");
    }
}

static AdminCommentDto ReadAdminComment(NpgsqlDataReader reader) => new(
    reader.GetGuid(0),
    reader.GetGuid(1),
    reader.GetString(2),
    reader.GetString(3),
    reader.GetInt32(4),
    reader.GetInt32(5),
    reader.GetFieldValue<DateTimeOffset>(6),
    reader.GetGuid(7),
    reader.IsDBNull(8) ? null : reader.GetGuid(8)
);

static void AddAdminUserFilters(NpgsqlCommand command, bool? banned, string search)
{
    if (banned is not null)
    {
        command.Parameters.AddWithValue("banned", banned.Value);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        command.Parameters.AddWithValue("search", $"%{search}%");
    }
}

static AdminUserDto ReadAdminUser(NpgsqlDataReader reader) => new(
    reader.GetGuid(0),
    reader.GetGuid(1),
    reader.GetString(2),
    reader.GetString(3),
    reader.GetInt32(4),
    reader.GetString(5),
    reader.GetBoolean(6),
    reader.GetBoolean(7),
    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
    reader.IsDBNull(9) ? null : reader.GetString(9),
    reader.GetFieldValue<DateTimeOffset>(10),
    reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
    Convert.ToInt32(reader.GetInt64(12)),
    Convert.ToInt32(reader.GetInt64(13))
);

static string HashBackupCode(string code)
{
    var normalized = code.ToUpperInvariant().Replace("-", "").Trim();
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(normalized)));
}

static string GenerateBackupSegment()
{
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    return new string(Enumerable.Range(0, 4)
        .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
        .ToArray());
}

// Bildirim okundu/tümünü oku aksiyonlarından sonra SSE kanalına publish et.
// notificationId null ise read-all, dolu ise tekil read senaryosudur.
static async Task PublishNotificationReadEventAsync(
    RedisService redis,
    NpgsqlConnection connection,
    Guid deviceId,
    Guid? notificationId)
{
    try
    {
        await using var userCmd = new NpgsqlCommand(
            "SELECT id FROM users WHERE device_id = @deviceId AND deleted_at IS NULL LIMIT 1",
            connection);
        userCmd.Parameters.AddWithValue("deviceId", deviceId);
        var userIdObj = await userCmd.ExecuteScalarAsync();
        if (userIdObj is not Guid userId) return;

        await using var countCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM notifications n
            LEFT JOIN posts p ON p.id = n.post_id
            WHERE n.device_id = @deviceId
              AND n.is_read = FALSE AND n.dismissed_at IS NULL
              AND (n.post_id IS NULL OR p.status = 'active')
            """,
            connection);
        countCmd.Parameters.AddWithValue("deviceId", deviceId);
        var unreadCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        await redis.PublishUserEventAsync(userId, new
        {
            type = "notification.read",
            notificationId,
            unreadCount
        });
    }
    catch { }
}
