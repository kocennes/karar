using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public enum UserAccessState
{
    Active,
    Banned,
    Deleted,
}

public interface IUserAccessStatusService
{
    Task<UserAccessState> GetStateAsync(Guid userId, CancellationToken cancellationToken = default);
}

// Kullanıcının ban/silinme durumunu istek seviyesinde okur. Access token 15 dk geçerli kaldığı için
// banlanan ya da hesabını silen kullanıcı bu sürede yazma yapamasın diye BannedUserMiddleware kullanır.
// Ban tanımı şifreli giriş ve refresh ile aynıdır: users.is_banned = TRUE.
public sealed class UserAccessStatusService(
    Db db,
    RedisService redis,
    ILogger<UserAccessStatusService> logger) : IUserAccessStatusService
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static string CacheKey(Guid userId) => $"user:access-state:{userId:N}";

    public static UserAccessState Evaluate(bool userExists, bool isBanned, bool isDeleted) =>
        !userExists || isDeleted ? UserAccessState.Deleted
        : isBanned ? UserAccessState.Banned
        : UserAccessState.Active;

    public async Task<UserAccessState> GetStateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(userId);
        try
        {
            var cached = await redis.GetDb().StringGetAsync(cacheKey);
            if (cached.HasValue && Enum.TryParse<UserAccessState>(cached.ToString(), out var cachedState))
                return cachedState;
        }
        catch { /* Redis erişilemezse DB'ye düş */ }

        UserAccessState state;
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT is_banned, deleted_at FROM users WHERE id = @userId",
                connection
            );
            command.Parameters.AddWithValue("userId", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            state = await reader.ReadAsync(cancellationToken)
                ? Evaluate(userExists: true, isBanned: reader.GetBoolean(0), isDeleted: !reader.IsDBNull(1))
                : Evaluate(userExists: false, isBanned: false, isDeleted: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB erişilemezse yazma handler'ı da çalışamaz; meşru trafiği burada kesmemek için fail-open.
            logger.LogWarning(ex, "UserAccessStatusService: kullanıcı durumu okunamadı, kontrol atlandı.");
            return UserAccessState.Active;
        }

        try { await redis.GetDb().StringSetAsync(cacheKey, state.ToString(), CacheTtl); }
        catch { /* önbellek en iyi çaba */ }

        return state;
    }
}
