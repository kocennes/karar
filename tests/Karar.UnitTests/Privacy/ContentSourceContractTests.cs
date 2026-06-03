using FluentAssertions;

namespace Karar.UnitTests.Privacy;

public sealed class ContentSourceContractTests
{
    [Fact]
    public void ContentSourceMigration_AddsColumnWithUserDefault()
    {
        var migrationText = TestRepoPaths.ReadText(
            "backend",
            "migrations",
            "V61__post_content_source.sql");

        migrationText.Should().Contain("content_source TEXT NOT NULL DEFAULT 'user'");
        migrationText.Should().Contain("CHECK (content_source IN ('user', 'system', 'ai'))");
    }

    [Fact]
    public void PostDto_ExposesContentSourceField()
    {
        var domainText = TestRepoPaths.ReadText(
            "backend",
            "Karar.Api",
            "Models",
            "Domain.cs");

        domainText.Should().Contain("ContentSource");
    }

    [Fact]
    public void ReadPostsAsync_ReadsContentSourceByName()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().Contain("ReadString(\"content_source\")");
        programText.Should().Contain("ContentSource: contentSource");
    }

    [Fact]
    public void FeedQueries_SelectContentSource()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().Contain("p.content_source");
    }

    [Fact]
    public void FlutterPostModel_HasContentSourceField()
    {
        var modelText = TestRepoPaths.ReadText("lib", "shared", "models", "post.dart");

        modelText.Should().Contain("contentSource");
        modelText.Should().Contain("'user'");
    }

    [Fact]
    public void FlutterPostCard_ShowsBadgeForNonUserContent()
    {
        var postCardText = TestRepoPaths.ReadText(
            "lib",
            "features",
            "feed",
            "post_card.dart");

        postCardText.Should().Contain("contentSource != 'user'");
        postCardText.Should().Contain("_ContentSourceBadge");
        postCardText.Should().Contain("AI destekli");
        postCardText.Should().Contain("Sistem");
    }

    [Fact]
    public void FlutterPostDetail_ShowsBadgeForNonUserContent()
    {
        var detailText = TestRepoPaths.ReadText(
            "lib",
            "features",
            "post_detail",
            "post_detail_screen.dart");

        detailText.Should().Contain("contentSource != 'user'");
        detailText.Should().Contain("_ContentSourceBadge");
    }
}
