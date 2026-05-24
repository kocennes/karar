using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Karar.Api.Common;
using Karar.Api.Common.Middleware;
using Karar.Api.Contracts;
using Karar.Api.Data;
using Karar.Api.Models;
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
builder.Services.AddSingleton<PlayIntegrityService>();
builder.Services.AddHostedService<ImageModerationWorker>();
builder.Services.AddSingleton<ReportThresholdService>();
builder.Services.AddSingleton<ReporterReputationService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<NotificationRateLimiter>();
builder.Services.AddSingleton<BruteForceService>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<FirebaseAuthService>();
builder.Services.AddSingleton<SseConnectionManager>();
builder.Services.AddHostedService<TrendScoreUpdater>();
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddSingleton<CommentNotificationBatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CommentNotificationBatcher>());
builder.Services.AddHostedService<VerdictReminderJob>();
builder.Services.AddHostedService<DataRetentionService>();
builder.Services.AddHostedService<PoliticalNarrativeClusterJob>();
builder.Services.AddHostedService<PostDistributionJob>();
builder.Services.AddHostedService<AuditLogExportJob>();
builder.Services.AddSingleton<CategoryThrottleService>();
builder.Services.AddSingleton<AffinityService>();
builder.Services.AddSingleton<GeoService>();
builder.Services.AddSingleton<DeviceTrustService>();
builder.Services.AddSingleton<ComplianceLogService>();
builder.Services.AddSingleton<BusinessMetricsService>();
builder.Services.AddScoped<RequestDevice>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global: 300 istek / dakika / IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
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

    // Admin login: 10 deneme / 15 dakika / IP (gerçek koruma DistributedRateLimitMiddleware'de Redis ile)
    options.AddPolicy("auth-strict", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
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

    // Cihaz başına saatte max 10 rapor
    options.AddPolicy("report-create", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }
        )
    );

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
        if (builder.Environment.IsDevelopment())
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
                : ["https://karar.app", "https://www.karar.app", "https://admin.karar.app", "https://judge-app-karar.web.app", "https://judge-app-karar.firebaseapp.com"];
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
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
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
    KnownNetworks = { new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("::ffff:0.0.0.0"), 96) }
});

app.UseResponseCompression();
app.UseCors("flutter-app");
app.UseMiddleware<RedactedRequestLoggingMiddleware>();

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

app.UseRateLimiter();
app.UseMiddleware<DistributedRateLimitMiddleware>();
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
    PlayIntegrityService integrity,
    RedisService redis,
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

    // Play Integrity verification (Android only, soft mode — doesn't block registration)
    var integrityFailed = false;
    if (request.Platform == "android" &&
        !string.IsNullOrWhiteSpace(request.IntegrityToken) &&
        !string.IsNullOrWhiteSpace(request.Nonce))
    {
        var result = await integrity.VerifyAsync(request.Nonce, request.IntegrityToken);
        if (result == false)
        {
            integrityFailed = true;
            log.LogWarning("DeviceRegister: Play Integrity doğrulaması başarısız. Fingerprint={F}", request.Fingerprint);
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

    // Record failed integrity to influence trust score
    if (integrityFailed)
    {
        await using var trustCmd = new NpgsqlCommand(
            """
            INSERT INTO device_trust_scores (device_id, failed_integrity_count)
            VALUES (@id, 1)
            ON CONFLICT (device_id) DO UPDATE
                SET failed_integrity_count = device_trust_scores.failed_integrity_count + 1,
                    is_suspicious = true,
                    suspicious_reason = 'integrity_failed'
            """,
            connection);
        trustCmd.Parameters.AddWithValue("id", deviceId);
        await trustCmd.ExecuteNonQueryAsync();
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
    Guid? afterId = null
) =>
{
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    var offset = (page - 1) * limit;
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
    var afterWhere = afterId is null
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

    // Diversity pass applies only to page 1, trending sort, no category filter, no afterId cursor.
    // Uses window functions to cap: ≤5 posts per category, ≤3 posts per author/device.
    var applyDiversityPass = page == 1 && sort == "trending" && categoryId is null && afterId is null;

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
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
              AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            """;
    }

    await using var countCommand = new NpgsqlCommand(countSql, connection);
    countCommand.Parameters.AddWithValue("userId", userParam);
    countCommand.Parameters.AddWithValue("stage1UcbSlots", stage1UcbSlots);
    if (deviceId is not null) countCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    if (!applyDiversityPass && categoryId is not null) countCommand.Parameters.AddWithValue("categoryId", categoryId.Value);
    if (!applyDiversityPass && afterId is not null) countCommand.Parameters.AddWithValue("afterId", afterId.Value);
    if (hasNotInterested) countCommand.Parameters.AddWithValue("notInterestedIds", notInterestedIds!.ToArray());
    if (hasOverImposed) countCommand.Parameters.AddWithValue("overImposedIds", overImposedIds!.ToArray());

    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    string feedSql;
    if (applyDiversityPass)
    {
        feedSql = $"""
            WITH base AS (
                SELECT p.id, p.title, p.content, p.image_url, c.id AS cat_id, c.name AS cat_name, c.emoji,
                       p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
                       v.vote_type, p.trend_score, p.created_at,
                       (p.device_id = @deviceId OR p.user_id = @userId),
                       p.is_anonymous AS is_owner,
                       ROW_NUMBER() OVER (PARTITION BY p.category_id ORDER BY p.trend_score DESC, p.created_at DESC) AS cat_rank,
                       ROW_NUMBER() OVER (PARTITION BY COALESCE(p.user_id::text, p.device_id::text) ORDER BY p.trend_score DESC, p.created_at DESC) AS author_rank
                FROM posts p
                JOIN categories c ON c.id = p.category_id
                LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
                WHERE p.status = 'active' AND p.is_unlisted = FALSE {stageWhere} {notInterestedWhere} {overImposedWhere}
                  AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
                  AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            )
            SELECT id, title, content, image_url, cat_id, cat_name, emoji,
                   vote_count_hakli, vote_count_haksiz, comment_count,
                   vote_type, trend_score, created_at, is_owner
            FROM base
            WHERE cat_rank <= 5 AND author_rank <= 3
            ORDER BY trend_score DESC, created_at DESC
            LIMIT @limit
            """;
    }
    else
    {
        // For trending sort with a logged-in user, apply affinity multiplier to trend score
        var affinityJoin = sort == "trending" && userId is not null
            ? "LEFT JOIN user_category_affinity uca ON uca.user_id = @userId AND uca.category_id = p.category_id"
            : "";
        var affinityScore = sort == "trending" && userId is not null
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
                   p.is_unlisted, p.is_anonymous, p.tags
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            {affinityJoin}
            WHERE p.status = 'active' AND p.is_unlisted = FALSE {categoryWhere} {afterWhere} {stageWhere} {notInterestedWhere} {overImposedWhere}
              AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
              AND NOT EXISTS (SELECT 1 FROM muted_categories mc WHERE mc.user_id = @userId AND mc.category_id = p.category_id)
            ORDER BY {affinityOrderBy}
            LIMIT @limit OFFSET @offset
            """;
    }

    var freshSlotTarget = applyDiversityPass ? Math.Max(1, (int)Math.Ceiling(limit * 0.20)) : 0;

    await using var command = new NpgsqlCommand(feedSql, connection);
    command.Parameters.AddWithValue("deviceId", deviceParam);
    command.Parameters.AddWithValue("userId", userParam);
    command.Parameters.AddWithValue("stage1UcbSlots", stage1UcbSlots);
    if (!applyDiversityPass && categoryId is not null) command.Parameters.AddWithValue("categoryId", categoryId.Value);
    if (!applyDiversityPass && afterId is not null) command.Parameters.AddWithValue("afterId", afterId.Value);
    if (hasNotInterested) command.Parameters.AddWithValue("notInterestedIds", notInterestedIds!.ToArray());
    if (hasOverImposed) command.Parameters.AddWithValue("overImposedIds", overImposedIds!.ToArray());
    command.Parameters.AddWithValue("limit", applyDiversityPass ? Math.Max(1, limit - freshSlotTarget) : limit);
    if (!applyDiversityPass) command.Parameters.AddWithValue("offset", offset);

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
                   p.is_unlisted, p.is_anonymous, p.tags
            FROM posts p
            JOIN categories c ON c.id = p.category_id
            LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
            WHERE p.status = 'active' AND p.is_unlisted = FALSE
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
                  AND p.distribution_stage >= 2
                  AND p.id != ALL(@existingIds)
                  AND p.category_id NOT IN (
                      SELECT DISTINCT category_id
                      FROM user_category_affinity
                      WHERE user_id = @userId
                        AND updated_at >= NOW() - INTERVAL '7 days'
                  )
                  AND NOT EXISTS (SELECT 1 FROM blocked_users bu WHERE bu.blocker_user_id = @userId AND bu.blocked_user_id = p.user_id)
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

    var response = new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total), rankingLabel);

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
               p.is_unlisted, p.is_anonymous, p.tags
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
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
               p.is_unlisted, p.is_anonymous, p.tags
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
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

    // Fresh: stage >= 2, < 12h old, 0-10 votes — new posts awaiting judgment
    await using var freshCmd = new NpgsqlCommand(
        """
        SELECT p.id, p.title, p.content, p.image_url, c.id, c.name, c.emoji,
               p.vote_count_hakli, p.vote_count_haksiz, p.comment_count,
               v.vote_type, p.trend_score, p.created_at,
               (p.device_id = @deviceId OR p.user_id = @userId),
               p.is_unlisted, p.is_anonymous, p.tags
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.status = 'active' AND p.is_unlisted = FALSE
          AND p.distribution_stage >= 2
          AND p.created_at >= NOW() - INTERVAL '12 hours'
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

    return Results.Ok(new DiscoverResponse(rising, controversial, fresh, cityTrending, city));
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
               p.is_unlisted, p.is_anonymous, p.tags
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
               p.is_unlisted, p.is_anonymous, p.tags
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
               p.is_unlisted, p.is_anonymous, p.tags
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
               p.is_unlisted, p.is_anonymous, p.tags
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
    BusinessMetricsService metrics
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

    if (await categoryThrottle.IsThrottledAsync(request.CategoryId))
    {
        return Results.Json(
            new ErrorEnvelope(new ErrorBody("CATEGORY_THROTTLED", "Bu kategori ÅŸu an yeni gÃ¶nderilere kapalÄ±dÄ±r. LÃ¼tfen daha sonra tekrar deneyin.")),
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }

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

    var moderation = moderationService.Analyze($"{request.Title}\n{request.Content}");
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
            tags
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
            @tags
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

    var postIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("post_create", postIp, effectiveDeviceId, userId, newPostId, "post",
        new { status = newPostStatus, category_id = request.CategoryId });

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
               p.is_unlisted, p.is_anonymous, p.tags, p.ai_summary
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = @id AND p.status = 'active'
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
               p.is_unlisted, p.is_anonymous, p.tags, p.ai_summary
        FROM posts p
        JOIN categories c ON c.id = p.category_id
        LEFT JOIN votes v ON v.post_id = p.id AND v.device_id = @deviceId
        WHERE p.id = @id AND p.status = 'active'
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
    command.Parameters.AddWithValue("postId", id);
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    command.Parameters.AddWithValue("dwellSeconds", dwellSeconds);
    command.Parameters.AddWithValue("dwellCount", dwellCount);
    command.Parameters.AddWithValue("interactedCount", interactedCount);

    try { await command.ExecuteNonQueryAsync(); } catch { /* ignore: post may not exist */ }
    return Results.NoContent();
});

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
app.MapPost("/api/v1/posts/{id:guid}/feedback", async (
    Guid id,
    PostFeedbackRequest request,
    HttpRequest httpRequest,
    RequestDevice requestDevice,
    RedisService redis
) =>
{
    if (request.Type is not ("not_interested" or "seen_too_much" or "sensitive"))
    {
        return BadRequest("INVALID_FEEDBACK_TYPE", "Geçersiz geri bildirim türü.");
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
    page = Math.Max(1, page);
    limit = Math.Clamp(limit, 1, 50);
    q = q.Trim();
    if (q.Length < 3)
    {
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
    return Results.Ok(new FeedResponse(posts, new Pagination(page, limit, total, offset + posts.Count < total)));
});

app.MapGet("/api/v1/search/users", async (
    string q,
    Db db,
    int limit = 20
) =>
{
    q = q.Trim();
    limit = Math.Clamp(limit, 1, 20);
    if (q.Length < 3)
    {
        return BadRequest("QUERY_TOO_SHORT", "Arama en az 3 karakter olmalı.");
    }

    await using var connection = await db.OpenConnectionAsync();
    await using var command = new NpgsqlCommand(
        """
        SELECT u.username,
               u.karma,
               (SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id AND p.status = 'active') AS post_count
        FROM users u
        WHERE u.deleted_at IS NULL
          AND u.is_banned = FALSE
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
    ComplianceLogService complianceLog
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
    var trustDecision = await deviceTrust.EvaluateForVoteAsync(connection, transaction, effectiveDeviceId.Value);

    int hakli, haksiz;
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

    var voteIp = httpRequest.HttpContext.Connection.RemoteIpAddress?.ToString();
    _ = complianceLog.LogAsync("vote", voteIp, effectiveDeviceId, userId, id, "post",
        new { vote_type = request.VoteType, quarantined = trustDecision.ShouldQuarantineVote });

    if (userId is not null && categoryId > 0)
        _ = affinity.RecordVoteAsync(userId.Value, categoryId);

    // Quarantined (suspicious) votes are excluded from trend_score by the SQL filter.
    // Delay trend propagation: only mark dirty for trusted votes so the
    // TrendScoreUpdater picks up quarantined ones on its next scheduled run.
    if (!trustDecision.ShouldQuarantineVote)
        await redis.MarkPostDirtyAsync(id);

    // City-level trending: fire-and-forget update
    var city = geo.GetCity(voterIp);
    if (city is not null && !trustDecision.ShouldQuarantineVote)
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
        _ => $"{wilsonExpr} DESC, cm.created_at ASC"
    };

    await using var connection = await db.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand(
        "SELECT COUNT(*) FROM comments WHERE post_id = @postId AND status = 'active' AND parent_id IS NULL",
        connection
    );
    countCommand.Parameters.AddWithValue("postId", id);
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

                UNION ALL

                SELECT {commentSelectColumns}
                FROM comments cm
                JOIN posts p ON p.id = cm.post_id
                JOIN descendants d ON d.id = cm.parent_id
                LEFT JOIN comment_upvotes cu ON cu.comment_id = cm.id AND cu.device_id = @deviceId
                LEFT JOIN comment_downvotes cd ON cd.comment_id = cm.id AND cd.device_id = @deviceId
                LEFT JOIN users u ON u.id = cm.user_id
                WHERE cm.status = 'active'
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
    ComplianceLogService complianceLog
) =>
{
    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    var userId = GetOptionalUserId(httpRequest, jwtService);
    if (deviceId is null && userId is null)
    {
        return Unauthorized();
    }

    var moderation = moderationService.Analyze(request.Content);
    if (moderation.IsRejected)
    {
        return BadRequest(moderation.Code ?? "CONTENT_REJECTED", moderation.Message);
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
        INSERT INTO comments (
            post_id,
            device_id,
            user_id,
            parent_id,
            content,
            status,
            moderation_reason,
            moderation_checked_at
        )
        VALUES (
            @postId,
            @deviceId,
            @userId,
            @parentId,
            @content,
            @status,
            @moderationReason,
            NOW()
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
        await commentNotificationBatcher.HandleNewCommentAsync(id, effectiveDeviceId.Value, commentId, request.ParentId);
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
                _ = affinity.RecordCommentAsync(userId.Value, catId);
        }
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
    RedisService redis
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

    if (request.Reason is not ("hate_speech" or "harassment" or "personal_info" or "spam" or "self_harm" or "illegal" or "other"))
    {
        return BadRequest("INVALID_REPORT_REASON", "Geçersiz rapor sebebi.");
    }

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);
    if (deviceId is null)
    {
        return Unauthorized();
    }

    // Cihaz bazlı sliding window: saatte max 10 rapor
    if (!await redis.IsAllowedAsync("report-device", deviceId.Value.ToString("N"), 10, TimeSpan.FromHours(1)))
    {
        return TooManyRequests("RATE_LIMIT_REPORTS", "Çok fazla şikayet gönderdiniz. Bir süre sonra tekrar deneyin.", 3600);
    }

    var reporterPrincipal = GetJwtPrincipal(httpRequest, jwtService);
    var reporterUserId = reporterPrincipal is null ? (Guid?)null : GetUserId(reporterPrincipal);

    await using var connection = await db.OpenConnectionAsync();
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
    return Results.Created($"/api/v1/reports/{reportId}", new ReportResponse(reportId, "Åikayetiniz alÄ±ndÄ±. Ä°ncelenecek."));
}).RequireRateLimiting("report-create");

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
    await using var countCommand = new NpgsqlCommand(
        "SELECT COUNT(*) FROM notifications WHERE device_id = @deviceId",
        connection
    );
    countCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

    await using var unreadCommand = new NpgsqlCommand(
        "SELECT COUNT(*) FROM notifications WHERE device_id = @deviceId AND is_read = FALSE",
        connection
    );
    unreadCommand.Parameters.AddWithValue("deviceId", deviceId.Value);
    var unreadCount = Convert.ToInt32(await unreadCommand.ExecuteScalarAsync());

    await using var command = new NpgsqlCommand(
        """
        SELECT id, type, title, body, post_id, is_read, created_at
        FROM notifications
        WHERE device_id = @deviceId
        ORDER BY created_at DESC
        LIMIT @limit OFFSET @offset
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    command.Parameters.AddWithValue("limit", limit);
    command.Parameters.AddWithValue("offset", offset);

    var notifications = new List<NotificationDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        notifications.Add(new NotificationDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6)
        ));
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
        """
        UPDATE notifications
        SET is_read = TRUE
        WHERE device_id = @deviceId AND is_read = FALSE
        """,
        connection
    );
    command.Parameters.AddWithValue("deviceId", deviceId.Value);
    await command.ExecuteNonQueryAsync();

    return Results.NoContent();
});

app.MapPost("/api/v1/admin/auth/login", async (
    AdminLoginRequest request,
    HttpContext httpContext,
    AdminAuthService adminAuth,
    BruteForceService bruteForce,
    Db db
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
    var bfIdentity = BruteForceService.IdentityFor(ip, "admin-login");

    if (await bruteForce.IsLockedOutAsync(bfIdentity))
    {
        return TooManyRequests("ACCOUNT_LOCKED", "Ã‡ok fazla baÅŸarÄ±sÄ±z deneme. 15 dakika bekleyin.");
    }

    if (!adminAuth.ValidateLogin(request.Email, request.Password, request.TotpCode))
    {
        await bruteForce.RecordFailedAttemptAsync(bfIdentity);
        await using var connection = await db.OpenConnectionAsync();
        await LogAdminActionAsync(connection, null!, request.Email, "login_failed", "auth", null, $"IP: {ip}");
        return Unauthorized();
    }

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
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
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
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
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
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
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
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
    {
        return Unauthorized();
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
    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var validSeverities = new HashSet<string> { "light", "medium", "heavy" };
    if (!validSeverities.Contains(request.Severity))
        return Results.BadRequest(new { error = "INVALID_SEVERITY", message = "Severity must be light, medium, or heavy." });

    var adminEmail = adminAuth.TryGetAdminEmail(httpRequest);
    if (adminEmail is null)
        return Unauthorized();

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

    await using var transaction = await connection.BeginTransactionAsync();

    await using var notifyCmd = new NpgsqlCommand(
        """
        INSERT INTO notifications (device_id, type, title, body, post_id)
        VALUES (@deviceId, 'moderation_result', @title, @body, @postId)
        """,
        connection,
        transaction
    );
    notifyCmd.Parameters.AddWithValue("deviceId", deviceId);
    notifyCmd.Parameters.AddWithValue("title", "İçeriğiniz Hakkında");
    notifyCmd.Parameters.AddWithValue("body", request.Message);
    notifyCmd.Parameters.AddWithValue("postId", targetType == "post" ? (object)targetId : DBNull.Value);
    await notifyCmd.ExecuteNonQueryAsync();

    await LogAdminActionAsync(connection, transaction, adminEmail, "notify_user", targetType, targetId, request.Message);
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
    if (ValidateRequest(request) is { } validationError)
        return validationError;

    var email = adminAuth.TryGetAdminEmail(httpRequest);
    if (email is null)
        return Unauthorized();

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
    ComplianceLogService complianceLog
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
    EmailService emailService
) =>
{
    if (ValidateRequest(request) is { } validationError)
    {
        return validationError;
    }

    var email = request.Email.ToLowerInvariant();
    await using var connection = await db.OpenConnectionAsync();

    // KullanÄ±cÄ± kayÄ±tlÄ± ve doÄŸrulanmamÄ±ÅŸ mÄ±?
    await using var checkCmd = new NpgsqlCommand(
        "SELECT 1 FROM users WHERE email = @email AND email_verified = FALSE",
        connection
    );
    checkCmd.Parameters.AddWithValue("email", email);
    if (await checkCmd.ExecuteScalarAsync() is null)
    {
        // GÃ¼venlik: var/yok bilgisi verme
        return Results.Ok(new MessageResponse("Kod yeniden gÃ¶nderildi."));
    }

    // Rate limit: son 60 saniyede OTP var mÄ±?
    await using var rateCmd = new NpgsqlCommand(
        "SELECT 1 FROM email_otps WHERE email = @email AND created_at > NOW() - INTERVAL '60 seconds'",
        connection
    );
    rateCmd.Parameters.AddWithValue("email", email);
    if (await rateCmd.ExecuteScalarAsync() is not null)
    {
        return BadRequest("OTP_RATE_LIMIT", "LÃ¼tfen 60 saniye bekleyip tekrar deneyin.");
    }

    // Eski OTP'leri temizle, yeni gÃ¶nder
    await using var deleteCmd = new NpgsqlCommand(
        "DELETE FROM email_otps WHERE email = @email",
        connection
    );
    deleteCmd.Parameters.AddWithValue("email", email);
    await deleteCmd.ExecuteNonQueryAsync();

    var otp = PasswordService.GenerateOtp();
    await using var insertCmd = new NpgsqlCommand(
        "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)",
        connection
    );
    insertCmd.Parameters.AddWithValue("email", email);
    insertCmd.Parameters.AddWithValue("hash", PasswordService.HashOtp(otp));
    await insertCmd.ExecuteNonQueryAsync();

    _ = emailService.SendOtpAsync(email, otp);
    return Results.Ok(new MessageResponse("Kod yeniden gÃ¶nderildi."));
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
    var bfIdentity = BruteForceService.IdentityFor(ip, "login");

    if (await bruteForce.IsLockedOutAsync(bfIdentity))
    {
        return TooManyRequests("ACCOUNT_LOCKED", "Ã‡ok fazla baÅŸarÄ±sÄ±z deneme. 15 dakika bekleyin.");
    }

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

    var deviceId = await requestDevice.TryGetDeviceIdAsync(httpRequest);

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
               u.username, p.tags
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
        "SELECT COUNT(*) FROM comments WHERE user_id = @userId AND status = 'active'",
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
        WHERE cm.user_id = @userId AND cm.status = 'active'
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
               p.is_unlisted, p.is_anonymous, p.tags
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
    EmailService emailService
) =>
{
    if (ValidateRequest(request) is { } validationError) return validationError;

    var email = request.Email.ToLowerInvariant();
    var otpKey = $"pwreset:{email}";

    await using var connection = await db.OpenConnectionAsync();

    // KullanÄ±cÄ± var mÄ± ve ÅŸifre hesabÄ± mÄ±?
    await using var userCmd = new NpgsqlCommand(
        "SELECT 1 FROM users WHERE email = @email AND auth_provider = 'password' AND deleted_at IS NULL",
        connection
    );
    userCmd.Parameters.AddWithValue("email", email);
    var exists = await userCmd.ExecuteScalarAsync();

    // KullanÄ±cÄ± yoksa yine de baÅŸarÄ±lÄ± dÃ¶ndÃ¼r (user enumeration Ã¶nleme)
    if (exists is null)
        return Results.Ok(new MessageResponse("EÄŸer bu e-posta kayÄ±tlÄ±ysa kod gÃ¶nderildi."));

    // Rate limit: son 60 saniyede kod gÃ¶nderilmiÅŸ mi?
    await using var rateLimitCmd = new NpgsqlCommand(
        "SELECT created_at FROM email_otps WHERE email = @email ORDER BY created_at DESC LIMIT 1",
        connection
    );
    rateLimitCmd.Parameters.AddWithValue("email", otpKey);
    var lastSent = await rateLimitCmd.ExecuteScalarAsync() as DateTimeOffset?;
    if (lastSent.HasValue && (DateTimeOffset.UtcNow - lastSent.Value).TotalSeconds < 60)
        return BadRequest("OTP_RATE_LIMIT", "LÃ¼tfen 60 saniye bekleyip tekrar deneyin.");

    // Eski OTP'leri temizle
    await using var deleteCmd = new NpgsqlCommand(
        "DELETE FROM email_otps WHERE email = @email",
        connection
    );
    deleteCmd.Parameters.AddWithValue("email", otpKey);
    await deleteCmd.ExecuteNonQueryAsync();

    var otp = PasswordService.GenerateOtp();
    await using var insertCmd = new NpgsqlCommand(
        "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)",
        connection
    );
    insertCmd.Parameters.AddWithValue("email", otpKey);
    insertCmd.Parameters.AddWithValue("hash", PasswordService.HashOtp(otp));
    await insertCmd.ExecuteNonQueryAsync();

    _ = emailService.SendPasswordResetOtpAsync(email, otp);
    return Results.Ok(new MessageResponse("EÄŸer bu e-posta kayÄ±tlÄ±ysa kod gÃ¶nderildi."));
}).RequireRateLimiting("auth-strict");

// â"€â"€ RESET PASSWORD â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

app.MapPost("/api/v1/auth/reset-password", async (
    ResetPasswordRequest request,
    Db db
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
        SELECT c.name, COUNT(p.id) as post_count
        FROM categories c
        LEFT JOIN posts p ON p.category_id = c.id AND p.status != 'deleted'
        GROUP BY c.id, c.name
        ORDER BY post_count DESC
        """,
        connection
    );

    var data = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        data.Add(new { name = reader.GetString(0), value = Convert.ToInt32(reader.GetInt64(1)) });
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
    });
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

// â"€â"€ 2FA Yedek KodlarÄ± â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
    EmailService emailService
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

    var otp = PasswordService.GenerateOtp();
    var otpHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp)));
    var otpKey = $"chgmail:{newEmail}";

    await using var delOld = new NpgsqlCommand("DELETE FROM email_otps WHERE email = @key", connection);
    delOld.Parameters.AddWithValue("key", otpKey);
    await delOld.ExecuteNonQueryAsync();

    await using var insertOtp = new NpgsqlCommand(
        "INSERT INTO email_otps (email, otp_hash) VALUES (@email, @hash)", connection
    );
    insertOtp.Parameters.AddWithValue("email", otpKey);
    insertOtp.Parameters.AddWithValue("hash", otpHash);
    await insertOtp.ExecuteNonQueryAsync();

    _ = emailService.SendChangeEmailOtpAsync(newEmail, otp);
    return Results.Ok(new { message = "Dogrulama kodu yeni e-posta adresinize gonderildi." });
}).RequireRateLimiting("auth-strict");

app.MapPost("/api/v1/auth/change-email/confirm", async (
    ConfirmChangeEmailRequest request,
    HttpRequest httpRequest,
    Db db,
    JwtService jwtService
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

    var reports = new List<ReportSignal>();
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var weight = ReporterReputationService.ComputeWeight(
                reader.GetInt32(3),
                reader.GetInt32(4)
            );
            reports.Add(new ReportSignal(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                weight
            ));
        }
    }

    var weightedReporters = reportThresholdService.CountWeightedIndependentReporters(reports);
    var weightedCritical  = reportThresholdService.CountWeightedIndependentReporters(
        reports.Where(r => reportThresholdService.IsCriticalReason(r.Reason))
    );

    return reportThresholdService.Evaluate(targetType, weightedReporters, weightedCritical);
}

static string? GetClientIpBlock(HttpRequest request)
{
    var ip = request.HttpContext.Connection.RemoteIpAddress;
    if (ip is null)
    {
        return null;
    }

    if (ip.IsIPv4MappedToIPv6)
    {
        ip = ip.MapToIPv4();
    }

    var bytes = ip.GetAddressBytes();
    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes.Length == 4)
    {
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
    }

    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && bytes.Length == 16)
    {
        return string.Join(':', Enumerable.Range(0, 4)
            .Select(i => BitConverter.ToUInt16(bytes.Skip(i * 2).Take(2).Reverse().ToArray(), 0).ToString("x"))) + "::/64";
    }

    return ip.ToString();
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
        SET status = 'auto_hidden',
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
        var isOwner = fc > 13 && !reader.IsDBNull(13) && reader.GetBoolean(13);
        bool isEdited, isSaved, isUnlisted, isAnonymous = false;
        string? authorName = null;
        Guid? authorId = null;
        IReadOnlyList<string>? tags = null;
        string? aiSummary = null;

        int anonIdx = -1;
        try { anonIdx = reader.GetOrdinal("is_anonymous"); } catch {}
        if (anonIdx != -1) isAnonymous = reader.GetBoolean(anonIdx);

        int statusIdx = -1;
        try { statusIdx = reader.GetOrdinal("status"); } catch {}
        string? status = statusIdx != -1 && !reader.IsDBNull(statusIdx) ? reader.GetString(statusIdx) : null;

        int modReasonIdx = -1;
        try { modReasonIdx = reader.GetOrdinal("moderation_reason"); } catch {}
        string? moderationReason = modReasonIdx != -1 && !reader.IsDBNull(modReasonIdx) ? reader.GetString(modReasonIdx) : null;

        if (fc >= 18)
        {
            isEdited = !reader.IsDBNull(14) && reader.GetBoolean(14);
            isSaved = !reader.IsDBNull(15) && reader.GetBoolean(15);
            authorName = reader.IsDBNull(16) ? null : reader.GetString(16);
            tags = reader.IsDBNull(17) ? null : reader.GetFieldValue<string[]>(17);
            isUnlisted = false;
        }
        else if (fc == 17)
        {
            isEdited = false;
            isSaved = false;
            isUnlisted = !reader.IsDBNull(14) && reader.GetBoolean(14);
            tags = reader.IsDBNull(15) ? null : reader.GetFieldValue<string[]>(15);
            aiSummary = reader.IsDBNull(16) ? null : reader.GetString(16);
        }
        else if (fc >= 16)
        {
            isEdited = false;
            isSaved = false;
            isUnlisted = !reader.IsDBNull(14) && reader.GetBoolean(14);
            tags = reader.IsDBNull(15) ? null : reader.GetFieldValue<string[]>(15);
        }
        else
        {
            isEdited = false;
            isSaved = false;
            isUnlisted = false;
        }

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
            AiSummary: aiSummary
        ));
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
    var hakliDelta = (newVote == "hakli" ? 1 : 0) - (oldVote == "hakli" ? 1 : 0);
    var haksizDelta = (newVote == "haksiz" ? 1 : 0) - (oldVote == "haksiz" ? 1 : 0);

    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET vote_count_hakli = vote_count_hakli + @hakliDelta,
            vote_count_haksiz = vote_count_haksiz + @haksizDelta,
            trend_score = (
                (
                    SELECT COUNT(*)
                    FROM votes v
                    WHERE v.post_id = posts.id AND v.is_quarantined = FALSE
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
    command.Parameters.AddWithValue("hakliDelta", hakliDelta);
    command.Parameters.AddWithValue("haksizDelta", haksizDelta);
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
        INSERT INTO votes (post_id, device_id, vote_type, voter_ip_block, is_quarantined, voter_region)
        VALUES (@postId, @deviceId, @voteType, @ipBlock, @isQuarantined, @voterRegion)
        ON CONFLICT (post_id, device_id)
        DO UPDATE SET
            vote_type = @voteType,
            voter_ip_block = COALESCE(@ipBlock, votes.voter_ip_block),
            voter_region = COALESCE(@voterRegion, votes.voter_region),
            is_quarantined = @isQuarantined,
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
    var hakliDelta = (newVote == "hakli" ? 1 : 0) - (oldVote == "hakli" ? 1 : 0);
    var haksizDelta = (newVote == "haksiz" ? 1 : 0) - (oldVote == "haksiz" ? 1 : 0);

    await using var command = new NpgsqlCommand(
        """
        UPDATE posts
        SET vote_count_hakli = vote_count_hakli + @hakliDelta,
            vote_count_haksiz = vote_count_haksiz + @haksizDelta,
            trend_score = (
                (
                    SELECT COUNT(*)
                    FROM votes v
                    WHERE v.post_id = posts.id AND v.is_quarantined = FALSE
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
    command.Parameters.AddWithValue("hakliDelta", hakliDelta);
    command.Parameters.AddWithValue("haksizDelta", haksizDelta);
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

    return (new CreatePostRequest(title, content, categoryId, null, isUnlisted, isAnonymous, tags), imageFile);
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
