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
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");

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
            var connection = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Username = _iamUser,
                Password = token,
            };
            return await NpgsqlDataSource.Create(connection.ConnectionString).OpenConnectionAsync();
        }

        return await NpgsqlDataSource.Create(_connectionString).OpenConnectionAsync();
    }

    private static async Task<string> GetCloudSqlLoginTokenAsync()
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        var scoped = credential.CreateScoped(CloudSqlLoginScope);
        return await ((ITokenAccess)scoped.UnderlyingCredential).GetAccessTokenForRequestAsync();
    }
}
