using System.Net;
using System.Text;
using FluentAssertions;
using Karar.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Karar.UnitTests.Moderation;

/// <summary>
/// Perspective API kullanılamadığında (yapılandırılmamış / zaman aşımı / HTTP hatası) içerik
/// moderasyonunun ne yapacağı: Moderation:ToxicityUnavailableMode "review" → incelemeye al
/// (Production varsayılanı), "allow" → yalnızca anahtar kelime filtresi (diğer ortamlar).
/// </summary>
public sealed class ToxicityUnavailableModeTests
{
    private const string ReviewMessage = "İçerik moderasyon incelemesine alındı.";
    private const string LongText = "Bu metin Perspective'in kısa metin eşiğini geçecek kadar uzun bir gönderi içeriğidir.";

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly PerspectiveResult CleanScore  = new(0.05f, 0.01f, 0.02f, 0.03f, 0.01f, 0.05f);
    private static readonly PerspectiveResult ReviewScore = new(0.75f, 0.10f, 0.20f, 0.60f, 0.30f, 0.75f);
    private static readonly PerspectiveResult RejectScore = new(0.95f, 0.90f, 0.50f, 0.80f, 0.70f, 0.95f);

    // ── ApplyToxicityResult: karar helper'ı ──────────────────────────────────

    [Theory]
    [InlineData(ToxicityCheckOutcome.NotConfigured)]
    [InlineData(ToxicityCheckOutcome.Failed)]
    public void Apply_Unavailable_ReviewMode_SendsActiveContentToReview(ToxicityCheckOutcome outcome)
    {
        var decision = PerspectiveApiService.ApplyToxicityResult(
            ModerationDecision.Active(), new ToxicityCheck(outcome, null), "review");

        decision.Status.Should().Be("under_review");
        decision.IsRejected.Should().BeFalse();
        decision.Code.Should().BeNull();
        decision.Message.Should().Be(ReviewMessage);
    }

    [Theory]
    [InlineData(ToxicityCheckOutcome.NotConfigured)]
    [InlineData(ToxicityCheckOutcome.Failed)]
    public void Apply_Unavailable_AllowMode_KeepsKeywordDecision(ToxicityCheckOutcome outcome)
    {
        var current = ModerationDecision.Active();

        var decision = PerspectiveApiService.ApplyToxicityResult(current, new ToxicityCheck(outcome, null), "allow");

        decision.Should().BeSameAs(current, because: "allow modu bugünkü davranıştır: yalnızca anahtar kelime filtresi");
    }

    [Fact]
    public void Apply_Unavailable_ReviewMode_IsCaseInsensitive()
    {
        var decision = PerspectiveApiService.ApplyToxicityResult(
            ModerationDecision.Active(), ToxicityCheck.Failed, "Review");

        decision.Status.Should().Be("under_review");
    }

    [Fact]
    public void Apply_SkippedShortText_IsNotUnavailable_KeepsDecisionEvenInReviewMode()
    {
        var current = ModerationDecision.Active();

        var decision = PerspectiveApiService.ApplyToxicityResult(current, ToxicityCheck.SkippedShortText, "review");

        decision.Should().BeSameAs(current, because: "kısa metin bilinçli atlamadır, API arızası değil");
    }

    [Fact]
    public void Apply_Unavailable_ReviewMode_KeepsExistingReviewDecision()
    {
        var current = ModerationDecision.CrisisReview();

        var decision = PerspectiveApiService.ApplyToxicityResult(current, ToxicityCheck.Failed, "review");

        decision.Should().BeSameAs(current, because: "kriz kodu ve bayrağı kaybolmamalı");
    }

    [Theory]
    [InlineData("review")]
    [InlineData("allow")]
    public void Apply_RejectScore_Rejects_RegardlessOfMode(string mode)
    {
        var decision = PerspectiveApiService.ApplyToxicityResult(
            ModerationDecision.Active(), ToxicityCheck.Scored(RejectScore), mode);

        decision.IsRejected.Should().BeTrue();
        decision.Status.Should().Be("rejected");
        decision.Code.Should().Be("CONTENT_REJECTED");
        decision.Message.Should().Be("İçerik politikası gereği bu metin yayınlanamaz.");
    }

    [Fact]
    public void Apply_ReviewScore_SendsActiveContentToReview()
    {
        var decision = PerspectiveApiService.ApplyToxicityResult(
            ModerationDecision.Active(), ToxicityCheck.Scored(ReviewScore), "allow");

        decision.Status.Should().Be("under_review");
        decision.IsRejected.Should().BeFalse();
        decision.Message.Should().Be(ReviewMessage);
    }

    [Fact]
    public void Apply_ReviewScore_KeepsExistingReviewDecision()
    {
        var current = ModerationDecision.Review("Kişisel bilgi olabilecek veri tespit edildi.");

        var decision = PerspectiveApiService.ApplyToxicityResult(current, ToxicityCheck.Scored(ReviewScore), "allow");

        decision.Should().BeSameAs(current, because: "daha açıklayıcı olan anahtar kelime gerekçesi korunmalı");
    }

    [Fact]
    public void Apply_CleanScore_KeepsDecision()
    {
        var current = ModerationDecision.Active();

        var decision = PerspectiveApiService.ApplyToxicityResult(current, ToxicityCheck.Scored(CleanScore), "review");

        decision.Should().BeSameAs(current);
    }

    [Fact]
    public void Apply_RejectedCurrent_IsReturnedUnchanged()
    {
        var current = ModerationDecision.Reject("CONTENT_REJECTED", "İçerik politikası gereği bu metin yayınlanamaz.");

        var decision = PerspectiveApiService.ApplyToxicityResult(current, ToxicityCheck.Scored(CleanScore), "allow");

        decision.Should().BeSameAs(current);
    }

    // ── ResolveUnavailableMode: ortam varsayılanları ─────────────────────────

    [Theory]
    [InlineData(null, true, "review")]
    [InlineData("", true, "review")]
    [InlineData(null, false, "allow")]
    [InlineData("allow", true, "allow")]
    [InlineData("review", false, "review")]
    [InlineData(" REVIEW ", false, "review")]
    [InlineData("bogus", true, "review")]
    [InlineData("bogus", false, "allow")]
    public void ResolveUnavailableMode_DefaultsByEnvironment(string? configured, bool isProduction, string expected)
    {
        PerspectiveApiService.ResolveUnavailableMode(configured, isProduction).Should().Be(expected);
    }

    [Fact]
    public void Service_InProduction_WithoutConfig_DefaultsToReview()
    {
        CreateService(environment: "Production").UnavailableMode.Should().Be("review",
            because: "Production'da toksisite kontrolü olmadan içerik yayınlanmamalı");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void Service_OutsideProduction_WithoutConfig_DefaultsToAllow(string environment)
    {
        CreateService(environment: environment).UnavailableMode.Should().Be("allow",
            because: "API anahtarı olmadan yerel geliştirme çalışmaya devam etmeli");
    }

    [Fact]
    public void Service_ExplicitConfig_OverridesEnvironmentDefault()
    {
        CreateService(environment: "Production", unavailableMode: "allow").UnavailableMode.Should().Be("allow");
        CreateService(environment: "Development", unavailableMode: "review").UnavailableMode.Should().Be("review");
    }

    // ── AnalyzeAsync: sonuç türleri ───────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_ReportsNotConfigured()
    {
        var check = await CreateService(apiKey: null).AnalyzeAsync(LongText);

        check.Outcome.Should().Be(ToxicityCheckOutcome.NotConfigured);
        check.Result.Should().BeNull();
        check.IsUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_ShortText_ReportsSkipped()
    {
        var check = await CreateService().AnalyzeAsync("kısa metin");

        check.Outcome.Should().Be(ToxicityCheckOutcome.SkippedShortText);
        check.IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeAsync_HttpClientTimeout_IsTreatedAsFailure_NotThrown()
    {
        // HttpClient.Timeout dolunca TaskCanceledException fırlar; bu çağıranın iptali değildir,
        // "API kullanılamıyor" sayılmalı ve gönderi oluşturma 500 ile çökmemeli.
        var service = CreateService(new ThrowingHandler(new TaskCanceledException("timeout", new TimeoutException())));

        var check = await service.AnalyzeAsync(LongText);

        check.Outcome.Should().Be(ToxicityCheckOutcome.Failed);
        check.IsUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService(new ThrowingHandler(new OperationCanceledException(cts.Token)));

        var act = async () => await service.AnalyzeAsync(LongText, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnalyzeAsync_HttpError_ReportsFailed()
    {
        var service = CreateService(new ResponseHandler(HttpStatusCode.ServiceUnavailable, "{}"));

        var check = await service.AnalyzeAsync(LongText);

        check.Outcome.Should().Be(ToxicityCheckOutcome.Failed);
    }

    [Fact]
    public async Task AnalyzeAsync_SuccessfulResponse_ReportsScored()
    {
        const string body = """
            {"attributeScores":{
              "TOXICITY":{"summaryScore":{"value":0.42}},
              "SEVERE_TOXICITY":{"summaryScore":{"value":0.10}},
              "IDENTITY_ATTACK":{"summaryScore":{"value":0.20}},
              "INSULT":{"summaryScore":{"value":0.30}},
              "THREAT":{"summaryScore":{"value":0.15}}}}
            """;
        var service = CreateService(new ResponseHandler(HttpStatusCode.OK, body));

        var check = await service.AnalyzeAsync(LongText);

        check.Outcome.Should().Be(ToxicityCheckOutcome.Scored);
        check.Result.Should().NotBeNull();
        check.Result!.Toxicity.Should().BeApproximately(0.42f, 0.001f);
        check.Result.Overall.Should().BeApproximately(0.42f, 0.001f);
    }

    // ── Loglama: "yapılandırılmamış" ile "başarısız" ayrı, dakikada en fazla bir uyarı ──

    [Fact]
    public async Task AnalyzeAsync_NotConfigured_WarnsOncePerMinute_AndNamesTheReason()
    {
        var logger = new CapturingLogger();
        var service = CreateService(apiKey: null, environment: "Production", logger: logger);

        for (var i = 0; i < 3; i++) await service.AnalyzeAsync(LongText);

        logger.Warnings.Should().ContainSingle(because: "uyarı istek başına değil dakikada bir yazılmalı")
            .Which.Should().Contain("yapılandırılmamış").And.Contain("review");
    }

    [Fact]
    public async Task AnalyzeAsync_Failed_WarnsOncePerMinute_AndNamesTheReason()
    {
        var logger = new CapturingLogger();
        var service = CreateService(new ResponseHandler(HttpStatusCode.ServiceUnavailable, "{}"), logger: logger);

        for (var i = 0; i < 3; i++) await service.AnalyzeAsync(LongText);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("HTTP 503").And.NotContain("yapılandırılmamış");
    }

    // ── Kaynak sözleşmesi: Program.cs çağrı noktaları ─────────────────────────

    [Fact]
    public void Program_EveryPerspectiveCall_IsAppliedThroughHelper()
    {
        var analyzeCalls = Count(ProgramText, "perspectiveService.AnalyzeAsync(");
        var applyCalls = Count(ProgramText, "PerspectiveApiService.ApplyToxicityResult(");

        analyzeCalls.Should().BeGreaterThanOrEqualTo(1, because: "gönderi oluşturma Perspective'i çağırır");
        applyCalls.Should().Be(analyzeCalls,
            because: "her Perspective çağrısının sonucu (kullanılamıyor dahil) ApplyToxicityResult ile karara uygulanmalı; handler'da ad-hoc null kontrolü olmamalı");
        ProgramText.Should().NotContain("PerspectiveApiService.DetermineStatus(",
            because: "eşik mantığı handler'larda tekrarlanmamalı");
    }

    [Fact]
    public void CreatePostHandler_AppliesToxicityWithConfiguredMode_ThenRejects()
    {
        var block = SliceEndpoint(ProgramText, "app.MapPost(\"/api/v1/posts\",");

        block.Should().Contain("perspectiveService.AnalyzeAsync(");
        var applyIndex = block.IndexOf(
            "PerspectiveApiService.ApplyToxicityResult(moderation, toxicityCheck, perspectiveService.UnavailableMode)",
            StringComparison.Ordinal);
        applyIndex.Should().BeGreaterThan(0, because: "karar helper'dan ve yapılandırılan modla geçmeli");
        block[applyIndex..].Should().Contain("if (moderation.IsRejected)",
            because: "Perspective reddi de anahtar kelime reddi gibi BadRequest ile dönmeli");
    }

    [Fact]
    public void CreateCommentHandler_PerspectiveCalls_IfAny_UseHelper()
    {
        var block = SliceEndpoint(ProgramText, "app.MapPost(\"/api/v1/posts/{id:guid}/comments\",");

        Count(block, "PerspectiveApiService.ApplyToxicityResult(")
            .Should().Be(Count(block, "perspectiveService.AnalyzeAsync("),
                because: "yorumlara Perspective eklenirse karar aynı helper'dan geçmeli");
    }

    [Fact]
    public void AppSettings_DoesNotPinToxicityUnavailableMode()
    {
        // appsettings.json her ortamda yüklenir; burada sabitlenirse Production'ın fail-closed
        // varsayılanı sessizce ezilir. Mod ortam değişkeniyle (Moderation__ToxicityUnavailableMode) verilmeli.
        TestRepoPaths.ReadText("backend", "Karar.Api", "appsettings.json")
            .Should().NotContain("ToxicityUnavailableMode");
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────────

    private static PerspectiveApiService CreateService(
        HttpMessageHandler? handler = null,
        string? apiKey = "test-key",
        string environment = "Development",
        string? unavailableMode = null,
        CapturingLogger? logger = null)
    {
        var values = new List<KeyValuePair<string, string?>>();
        if (apiKey is not null) values.Add(new("Perspective:ApiKey", apiKey));
        if (unavailableMode is not null) values.Add(new(PerspectiveApiService.UnavailableModeConfigKey, unavailableMode));

        return new PerspectiveApiService(
            new StubHttpClientFactory(handler ?? new ThrowingHandler(new InvalidOperationException("HTTP çağrısı beklenmiyordu."))),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new StubHostEnvironment(environment),
            logger ?? new CapturingLogger());
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string SliceEndpoint(string text, string startMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf("\napp.Map", start + startMarker.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Karar.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingLogger : ILogger<PerspectiveApiService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IEnumerable<string> Warnings => _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));
    }
}
