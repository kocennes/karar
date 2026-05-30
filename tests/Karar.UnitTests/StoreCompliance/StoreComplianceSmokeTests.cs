using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.StoreCompliance;

public sealed class StoreComplianceSmokeTests
{
    [Fact]
    public void RegistrationAndPostCreation_RequirePolicyAcceptance()
    {
        var contracts = TestRepoPaths.ReadText("backend", "Karar.Api", "Contracts", "Requests.cs");
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        contracts.Should().Contain("bool AcceptedTerms = false");
        contracts.Should().Contain("bool AcceptedCommunityGuidelines = false");
        program.Should().Contain("POLICY_ACCEPTANCE_REQUIRED");

        Slice(program, "app.MapPost(\"/api/v1/posts\"", "await using var limitCmd = new NpgsqlCommand")
            .Should().Contain("!request.AcceptedTerms || !request.AcceptedCommunityGuidelines");

        Slice(program, "app.MapPost(\"/api/v1/auth/register\"", "await using var connection = await db.OpenConnectionAsync")
            .Should().Contain("!request.AcceptedTerms || !request.AcceptedCommunityGuidelines");
    }

    [Fact]
    public void StoreComplianceUserActions_AreBackedByRealEndpoints()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("app.MapPost(\"/api/v1/reports\"");
        program.Should().Contain("INSERT INTO reports");
        program.Should().Contain("app.MapPost(\"/api/v1/users/me/blocked\"");
        program.Should().Contain("INSERT INTO blocked_users");
        program.Should().Contain("app.MapDelete(\"/api/v1/users/me/blocked/{blockedId:guid}\"");
    }

    [Fact]
    public void HiddenRemovedAndUnderReviewContent_AreExcludedFromDiscoverySurfaces()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var feedBlock = Slice(program, "app.MapGet(\"/api/v1/posts\", async (", "var freshSlotTarget");
        var discoverBlock = Slice(program, "app.MapGet(\"/api/v1/posts/discover\"", "app.MapGet(\"/api/v1/posts/{id:guid}\"");
        var postBlock = Slice(program, "app.MapGet(\"/api/v1/posts/{id:guid}\"", "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\"");
        var notificationBlock = Slice(program, "app.MapGet(\"/api/v1/notifications\"", "app.MapPut(\"/api/v1/notifications/read-all\"");

        feedBlock.Should().Contain("p.status = 'active'");
        discoverBlock.Should().Contain("p.status = 'active'");
        postBlock.Should().Contain("p.status = 'active'");
        notificationBlock.Should().Contain("LEFT JOIN posts p ON p.id = n.post_id");
        notificationBlock.Should().Contain("n.post_id IS NULL OR p.status = 'active'");
    }

    [Fact]
    public void ClientScreens_SurfaceRequiredComplianceFlows()
    {
        var register = TestRepoPaths.ReadText("lib", "features", "auth", "register_screen.dart");
        var createPost = TestRepoPaths.ReadText("lib", "features", "create_post", "create_post_screen.dart");
        var postDetail = TestRepoPaths.ReadText("lib", "features", "post_detail", "post_detail_screen.dart");
        var report = TestRepoPaths.ReadText("lib", "features", "report", "report_bottom_sheet.dart");

        register.Should().Contain("acceptedTerms");
        register.Should().Contain("/legal/terms");
        register.Should().Contain("/legal/community");
        createPost.Should().Contain("acceptedCommunityGuidelines");
        createPost.Should().Contain("/legal/terms");
        createPost.Should().Contain("/legal/community");
        report.Should().Contain("widget.repository.report");
        postDetail.Should().Contain("ContentUnavailableView");
        postDetail.Should().Contain("under_review");
        postDetail.Should().Contain("auto_hidden");
        postDetail.Should().Contain("_confirmBlock");
    }

    [Fact]
    public void ReleaseGateCommand_IsDocumentedAndRunnable()
    {
        var script = TestRepoPaths.ReadText("scripts", "run-store-compliance-smoke.ps1");

        script.Should().Contain("store-compliance-smoke");
        script.Should().Contain("dotnet test tests/Karar.UnitTests/Karar.UnitTests.csproj");
        script.Should().Contain("flutter test --no-pub test/release_store_compliance_smoke_test.dart");

        if (!TestRepoPaths.TryReadText(out var releaseDoc, "docs", "app-store-release.md")) return;
        releaseDoc.Should().Contain("scripts/run-store-compliance-smoke.ps1");
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");

        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }
}
