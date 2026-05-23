using Npgsql;

namespace Karar.Api.Data;

public static class MigrationsRunner
{
    public static async Task RunAsync(string connectionString, string migrationsPath)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await EnsureMigrationsTableAsync(connection);

        var scripts = Directory.GetFiles(migrationsPath, "V*.sql")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var scriptPath in scripts)
        {
            var version = Path.GetFileNameWithoutExtension(scriptPath);
            if (await IsAppliedAsync(connection, version))
            {
                Console.WriteLine($"[migrate] {version}: already applied, skipping.");
                continue;
            }

            Console.WriteLine($"[migrate] Applying {version}...");
            var sql = await File.ReadAllTextAsync(scriptPath);

            await using var transaction = await connection.BeginTransactionAsync();
            await using (var cmd = new NpgsqlCommand(sql, connection, transaction))
            {
                await cmd.ExecuteNonQueryAsync();
            }
            await MarkAppliedAsync(connection, version, transaction);
            await transaction.CommitAsync();
            Console.WriteLine($"[migrate] {version}: done.");
        }

        Console.WriteLine("[migrate] All migrations applied.");
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection connection)
    {
        await using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """,
            connection
        );
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsAppliedAsync(NpgsqlConnection connection, string version)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = @version)",
            connection
        );
        cmd.Parameters.AddWithValue("version", version);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task MarkAppliedAsync(
        NpgsqlConnection connection,
        string version,
        NpgsqlTransaction transaction)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO schema_migrations (version) VALUES (@version) ON CONFLICT DO NOTHING",
            connection,
            transaction
        );
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync();
    }
}
