using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.Security;

/// scripts/seed_sample_posts.ps1 bir dönem production'a X-Admin-Token ile giden sabit
/// bir admin token içeriyordu (origin/main'de hâlâ duruyor). Repo'daki script ve infra
/// dosyaları kimlik bilgisi taşımamalı: token'lar ortam değişkeni/parametre ile gelir.
public sealed class RepoScriptsSecretContractTests
{
    private static readonly string[] ScannedRoots = ["scripts", "infra", ".github"];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".sh", ".bash", ".js", ".mjs", ".ts", ".py", ".yml", ".yaml", ".json", ".http",
    };

    private static readonly (string Name, Regex Pattern)[] Rules =
    [
        // "X-Admin-Token" = "literal"  /  'X-Admin-Token': 'literal'
        ("literal X-Admin-Token header value",
            new Regex("""["']X-Admin-Token["']\s*[=:]\s*["'][^"'$\s]+["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        // curl -H "X-Admin-Token: literal"
        ("literal X-Admin-Token curl header",
            new Regex("""X-Admin-Token:\s*(?![$\s"'])[^"'\s]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        // $adminToken = "literal"  /  KARAR_ADMIN_TOKEN='literal'  /  "adminToken": "literal"
        ("admin token variable assigned a literal",
            new Regex("""admin_?token["']?\s*[=:]\s*["'][^"'$\s{}]{4,}["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        // Sabit JWT (header.payload.signature)
        ("hard-coded JWT",
            new Regex(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")),
    ];

    [Theory]
    [InlineData("$adminToken = \"some-static-value123\"")]
    [InlineData("$headers = @{ \"X-Admin-Token\" = \"some-static-value123\" }")]
    [InlineData("curl -H \"X-Admin-Token: some-static-value123\" https://api.example.test")]
    [InlineData("KARAR_ADMIN_TOKEN='some-static-value123'")]
    [InlineData("{ \"adminToken\": \"some-static-value123\" }")]
    [InlineData("const headers = { 'X-Admin-Token': 'some-static-value123' };")]
    [InlineData("Authorization = \"Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhZG1pbiJ9.c2lnbmF0dXJlLXZhbHVl\"")]
    public void Detector_FlagsHardCodedAdminCredential(string line)
    {
        FindViolations(line).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("# Not: Statik admin token (X-Admin-Token) artik yok.")]
    [InlineData("$adminToken = $env:KARAR_ADMIN_TOKEN")]
    [InlineData("$headers = @{ \"X-Admin-Token\" = $adminToken }")]
    [InlineData("curl -H \"X-Admin-Token: $ADMIN_TOKEN\" \"$API_URL/api/v1/admin/stats\"")]
    [InlineData("ADMIN_TOKEN=\"${{ secrets.ADMIN_TOKEN }}\"")]
    [InlineData("\"X-Device-Token\" = $deviceToken")]
    public void Detector_AllowsReferencesWithoutLiteralSecret(string line)
    {
        FindViolations(line).Should().BeEmpty();
    }

    [Fact]
    public void RepoScriptsAndInfra_ContainNoHardCodedAdminCredentials()
    {
        var violations = new List<string>();
        foreach (var root in ScannedRoots)
        {
            var directory = TestRepoPaths.FilePath(root);
            if (!Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                         .Where(f => ScannedExtensions.Contains(Path.GetExtension(f))))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    // Değer raporlanmaz; yalnızca dosya:satır ve kural adı.
                    violations.AddRange(FindViolations(lines[i]).Select(rule =>
                        $"{Path.GetRelativePath(TestRepoPaths.Root, file)}:{i + 1} ({rule})"));
                }
            }
        }

        violations.Should().BeEmpty(
            "admin credentials must come from environment variables or parameters, never from committed files");
    }

    private static IEnumerable<string> FindViolations(string line) =>
        Rules.Where(rule => rule.Pattern.IsMatch(line)).Select(rule => rule.Name);
}
