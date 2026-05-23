using Npgsql;
using Google.Apis.Auth.OAuth2;

namespace Karar.Api.Data;

public sealed class Db
{
    private const string CloudSqlLoginScope = "https://www.googleapis.com/auth/sqlservice.login";
    private readonly string _connectionString;
    private readonly bool _useIamAuth;
    private readonly string? _iamUser;

    public Db(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");

        _connectionString = ConvertToKeyValue(raw);
        _useIamAuth = configuration.GetValue<bool>("Database:UseIamAuth");
        _iamUser = configuration["Database:IamUser"];
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        if (_useIamAuth)
        {
            if (string.IsNullOrWhiteSpace(_iamUser))
                throw new InvalidOperationException("Database:IamUser is required when Database:UseIamAuth=true.");

            var token = await GetCloudSqlLoginTokenAsync();
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Username = _iamUser,
                Password = token,
            };
            return await NpgsqlDataSource.Create(builder.ConnectionString).OpenConnectionAsync();
        }

        return await NpgsqlDataSource.Create(_connectionString).OpenConnectionAsync();
    }

    // Neon ve benzeri sağlayıcılar postgresql:// URI formatı döner;
    // Npgsql 6.x bunu NpgsqlConnectionStringBuilder'a doğrudan kabul etmez.
    private static string ConvertToKeyValue(string connectionString)
    {
        if (!connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        var sslMode = "Require";
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var parts = uri.Query.TrimStart('?').Split('&');
            foreach (var part in parts)
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    sslMode = Uri.UnescapeDataString(kv[1]);
            }
        }

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode={sslMode};Trust Server Certificate=true";
    }

    private static async Task<string> GetCloudSqlLoginTokenAsync()
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        var scoped = credential.CreateScoped(CloudSqlLoginScope);
        return await ((ITokenAccess)scoped.UnderlyingCredential).GetAccessTokenForRequestAsync();
    }
}
