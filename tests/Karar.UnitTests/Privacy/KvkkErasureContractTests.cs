using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.Privacy;

/// KVKK erasure / access regression tests.
///
/// Erasure and export are raw SQL that needs Postgres, so these tests check the SQL
/// against the migration schema and the handlers' source contracts.
public sealed class KvkkErasureContractTests
{
    // ── Erasure must not violate the users schema ──────────────────────────

    [Fact]
    public void UsersNotNullColumns_AreParsedFromMigrations()
    {
        var notNull = UsersNotNullColumns();

        notNull.Should().Contain(["email", "username", "device_id", "notification_preferences", "is_2fa_enabled"]);
        notNull.Should().NotContain(["bio", "date_of_birth", "gender", "totp_secret", "password_hash", "google_id"]);
    }

    [Fact]
    public void AdminKvkkDelete_DoesNotAssignNullToNotNullUserColumns()
    {
        var notNull = UsersNotNullColumns();
        var sources = new[] { AdminDeleteHandler(), ErasureRoutine() };

        foreach (var source in sources)
        {
            foreach (var column in NullAssignmentsInUserUpdates(source))
            {
                notNull.Should().NotContain(column,
                    $"users.{column} is NOT NULL, so 'SET {column} = NULL' makes every KVKK erasure fail with a not-null violation");
            }
        }
    }

    [Fact]
    public void AdminDeleteAndRetentionJob_ShareOneErasureRoutine()
    {
        AdminDeleteHandler().Should().Contain("UserDataErasure.EraseAsync(connection, transaction, id)");
        // self-deleted accounts (30-day recovery window) can still be erased on request
        AdminDeleteHandler().Should().Contain("email NOT LIKE 'deleted-%@deleted.karar.local'");
        AdminDeleteHandler().Should().NotContain("WHERE id = @id AND deleted_at IS NULL");
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "DataRetentionService.cs")
            .Should().Contain("UserDataErasure.EraseAsync(connection, transaction, userId, ct)");
    }

    [Fact]
    public void ErasureRoutine_ClearsAllDirectIdentifiersAndProfileData()
    {
        var erasure = ErasureRoutine();

        erasure.Should().Contain("email = 'deleted-' || replace(id::text, '-', '') || '@deleted.karar.local'");
        erasure.Should().Contain("username = 'silinen_'");
        foreach (var column in new[] { "password_hash", "google_id", "ban_reason", "totp_secret", "bio", "date_of_birth", "gender" })
        {
            erasure.Should().Contain($"{column} = NULL");
        }
        erasure.Should().Contain("notification_preferences = '{}'::jsonb");
        erasure.Should().Contain("is_2fa_enabled = FALSE");
        erasure.Should().Contain("fingerprint = 'deleted-'");
    }

    [Fact]
    public void ErasureRoutine_UnlinksSessionsListsAndIpDerivedData()
    {
        var erasure = ErasureRoutine();

        erasure.Should().Contain("DELETE FROM email_otps");
        erasure.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW()");
        erasure.Should().Contain("DELETE FROM totp_backup_codes");
        erasure.Should().Contain("UPDATE account_recovery_tokens SET used_at = NOW()");
        erasure.Should().Contain("DELETE FROM saved_posts");
        erasure.Should().Contain("DELETE FROM followed_categories");
        erasure.Should().Contain("DELETE FROM muted_categories");
        erasure.Should().Contain("DELETE FROM user_category_affinity");
        erasure.Should().Contain("DELETE FROM blocked_users WHERE blocker_user_id = @userId");
        erasure.Should().Contain("DELETE FROM user_device_links");
        erasure.Should().Contain("UPDATE votes SET voter_ip_block = NULL, voter_region = NULL");
        erasure.Should().Contain("UPDATE reports SET reporter_ip_block = NULL");
        erasure.Should().Contain("UPDATE feedback_items SET contact_email = NULL");
        // 5651 sayılı Kanun: compliance_logs append-only, yasal saklama süresince silinmez.
        erasure.Should().NotContain("UPDATE compliance_logs");
        erasure.Should().NotContain("DELETE FROM compliance_logs");
    }

    // ── Recovery window: deleted accounts are not shown by name ────────────

    [Fact]
    public void Comments_HideAuthorOfDeletedAccountDuringRecoveryWindow()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var listColumns = Slice(program, "const string commentSelectColumns", "static CommentDto ReadCommentRow");
        listColumns.Should().Contain("OR u.deleted_at IS NOT NULL THEN NULL ELSE u.username END AS username");
        listColumns.Should().Contain("OR u.deleted_at IS NOT NULL THEN NULL ELSE cm.user_id END AS author_id");

        var single = Slice(program, "static async Task<CommentDto?> ReadCommentDtoAsync", "AuthorName: reader.IsDBNull(9)");
        single.Should().Contain("OR u.deleted_at IS NOT NULL THEN NULL ELSE u.username END");
        single.Should().Contain("OR u.deleted_at IS NOT NULL THEN NULL ELSE cm.user_id END");
        // reaction responses must not reveal an anonymous post author (same rule as the list)
        single.Should().Contain("(p.is_anonymous = TRUE AND cm.user_id = p.user_id)");
    }

    // ── Access request (KVKK md. 11) ───────────────────────────────────────

    [Fact]
    public void DataExport_CoversEveryPersonalDataCategory()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var export = Slice(program,
            "app.MapGet(\"/api/v1/users/me/data-export\"",
            "app.MapGet(\"/api/v1/users/me/moderation-history\"");

        foreach (var fragment in new[]
                 {
                     "date_of_birth", "gender", "bio", "notification_preferences", "auth_provider",
                     "FROM votes", "voter_ip_block", "voter_region",
                     "FROM reports", "reporter_user_id = @id",
                     "FROM saved_posts", "FROM followed_categories", "FROM muted_categories",
                     "FROM blocked_users", "FROM user_device_links",
                     "FROM moderation_appeals", "FROM user_strikes", "FROM feedback_items",
                 })
        {
            export.Should().Contain(fragment);
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string AdminDeleteHandler()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        return Slice(program,
            "app.MapPost(\"/api/v1/admin/users/{id:guid}/delete\"",
            "app.MapPost(\"/api/v1/admin/users/bulk\"");
    }

    private static string ErasureRoutine() =>
        TestRepoPaths.TryReadText(out var text, "backend", "Karar.Api", "Services", "UserDataErasure.cs")
            ? text
            : string.Empty;

    private static IEnumerable<string> NullAssignmentsInUserUpdates(string source)
    {
        foreach (Match update in Regex.Matches(source, @"UPDATE users\b(?<body>.*?)\bWHERE\b", RegexOptions.Singleline))
        {
            foreach (Match assignment in Regex.Matches(update.Groups["body"].Value, @"\b(?<col>[a-z_0-9]+)\s*=\s*NULL\b"))
            {
                yield return assignment.Groups["col"].Value;
            }
        }
    }

    private static HashSet<string> UsersNotNullColumns()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(TestRepoPaths.FilePath("backend", "migrations"), "V*.sql"))
        {
            var sql = File.ReadAllText(file);

            var create = Regex.Match(sql, @"CREATE TABLE (?:IF NOT EXISTS )?users\s*\((?<body>.*?)\n\);",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (create.Success)
            {
                foreach (var line in create.Groups["body"].Value.Split('\n'))
                {
                    var column = Regex.Match(line, @"^\s*(?<col>[a-z_0-9]+)\s+[A-Z]+.*\bNOT NULL\b");
                    if (column.Success)
                    {
                        result.Add(column.Groups["col"].Value);
                    }
                }
            }

            foreach (Match alter in Regex.Matches(sql, @"ALTER TABLE (?:IF EXISTS )?users\b(?<body>[^;]*);", RegexOptions.IgnoreCase))
            {
                foreach (Match add in Regex.Matches(alter.Groups["body"].Value,
                             @"ADD COLUMN (?:IF NOT EXISTS )?(?<col>[a-z_0-9]+)(?<def>[^,]*)", RegexOptions.IgnoreCase))
                {
                    if (add.Groups["def"].Value.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(add.Groups["col"].Value);
                    }
                }
            }
        }
        return result;
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"missing start marker {start}");
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"missing end marker {end}");
        return text[startIndex..endIndex];
    }
}
