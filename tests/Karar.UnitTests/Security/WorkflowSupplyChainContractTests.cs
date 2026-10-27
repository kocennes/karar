using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.Security;

/// CI iş akışları üçüncü taraf action'lar çalıştırır (biri Firebase deploy servis hesabı
/// anahtarını alır). Açık bir permissions bloğu olmayan job'lar repo varsayılan GITHUB_TOKEN
/// kapsamını devralır; '3.x' gibi kayan bir Flutter sürümü de her koşuda farklı bir SDK çeker.
public sealed class WorkflowSupplyChainContractTests
{
    public static TheoryData<string> Workflows()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(TestRepoPaths.FilePath(".github", "workflows"), "*.yml"))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Workflow_DeclaresReadOnlyTopLevelTokenPermissions(string workflow)
    {
        var text = ReadWorkflow(workflow);

        var topLevel = Regex.Match(text, @"^permissions:[ \t]*\r?\n((?:[ \t]+.*\r?\n)+)", RegexOptions.Multiline);
        topLevel.Success.Should().BeTrue(
            $"{workflow} must set a top-level 'permissions:' block so jobs without their own block do not inherit the repository default token scope");
        topLevel.Groups[1].Value.Should().Contain("contents: read",
            $"{workflow} top-level token scope must be read-only");
        topLevel.Groups[1].Value.Should().NotContain("write",
            $"{workflow} must grant write scopes per job, not workflow-wide");
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Workflow_PinsExactFlutterVersion(string workflow)
    {
        foreach (Match match in Regex.Matches(ReadWorkflow(workflow), @"flutter-version:\s*['""]?([^'""\s]+)"))
        {
            match.Groups[1].Value.Should().MatchRegex(@"^\d+\.\d+\.\d+$",
                $"{workflow}: a floating Flutter version ('3.x') pulls a different SDK on every run");
        }
    }

    private static string ReadWorkflow(string workflow) =>
        TestRepoPaths.ReadText(".github", "workflows", workflow);
}
