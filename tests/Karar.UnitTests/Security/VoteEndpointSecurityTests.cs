using FluentAssertions;

namespace Karar.UnitTests.Security;

public sealed class VoteEndpointSecurityTests
{
    [Fact]
    public void VoteEndpoint_AddsSmallRandomTimingJitterBeforeBranching()
    {
        var programText = ReadProgram();
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        voteBlock.Should().Contain("await AddVoteTimingJitterAsync(httpRequest.HttpContext.RequestAborted);");
        voteBlock.IndexOf("AddVoteTimingJitterAsync", StringComparison.Ordinal)
            .Should().BeLessThan(voteBlock.IndexOf("ValidateRequest(request)", StringComparison.Ordinal));
        voteBlock.Should().Contain("return Results.Ok(response);");
    }

    [Fact]
    public void VoteTimingJitter_UsesFiveToFiftyMilliseconds()
    {
        var programText = ReadProgram();
        var jitterBlock = SliceBlock(
            programText,
            "static Task AddVoteTimingJitterAsync",
            "static async Task<(string? OldVote, int OldTotal");

        jitterBlock.Should().Contain("Random.Shared.Next(5, 51)");
        jitterBlock.Should().Contain("Task.Delay");
    }

    private static string ReadProgram()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "backend", "Karar.Api", "Program.cs"));
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TODO.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
