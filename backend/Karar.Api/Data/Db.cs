using Npgsql;
using Google.Apis.Auth.OAuth2;
using Karar.Api.Observability;

namespace Karar.Api.Data;

public sealed class Db : IDisposable
{
    private const string CloudSqlLoginScope = "https://www.googleapis.com/auth/sqlservice.login";
    private readonly string _connectionString;
    private readonly bool _useIamAuth;
    private readonly string? _iamUser;
    // Singleton data source — built once with tracing enabled for non-IAM path.
    // IAM path rotates the password on every call so a singleton cannot be used there.
    private readonly NpgsqlDataSource? _dataSource;

    public Db(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");

        _connectionString = ConvertToKeyValue(raw);
        _useIamAuth = configuration.GetValue<bool>("Database:UseIamAuth");
        _iamUser = configuration["Database:IamUser"];

        if (!_useIamAuth)
        {
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString).Build();
        }
    }

    public void Dispose() => _dataSource?.Dispose();

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        if (_useIamAuth)
        {
            if (string.IsNullOrWhiteSpace(_iamUser))
                throw new InvalidOperationException("Database:IamUser is required when Database:UseIamAuth=true.");

            var token = await GetCloudSqlLoginTokenAsync();
            var iamCs = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Username = _iamUser,
                Password = token,
            }.ConnectionString;
            // IAM path: short-lived data source because the token rotates on every call.
            // Npgsql holds an internal reference until the connection closes, so disposal is safe.
            return await new NpgsqlDataSourceBuilder(iamCs).Build().OpenConnectionAsync();
        }

        return await _dataSource!.OpenConnectionAsync();
    }

    // Neon ve benzeri sağlayıcılar postgresql:// URI formatı döner;
    // Npgsql 6.x bunu NpgsqlConnectionStringBuilder'a doğrudan kabul etmez.
    internal static string ConvertToKeyValue(string connectionString)
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
