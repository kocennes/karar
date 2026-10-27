using System.Reflection;
using StackExchange.Redis;

namespace Karar.UnitTests.Security;

/// Minimal in-memory IDatabase for behavioural tests of Redis-backed counters
/// (SubnetBanService, DeviceRegistrationRateLimit, BruteForceService).
/// Only the commands used by the services under test are implemented; any other
/// call throws so a test never silently passes against an unimplemented command.
public class InMemoryRedisDatabase : DispatchProxy
{
    private readonly Dictionary<string, RedisValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _sets = new(StringComparer.Ordinal);

    public static (IDatabase Database, InMemoryRedisDatabase State) Create()
    {
        var database = Create<IDatabase, InMemoryRedisDatabase>();
        return (database, (InMemoryRedisDatabase)(object)database);
    }

    public long Counter(string key) =>
        _values.TryGetValue(key, out var value) && !value.IsNull ? (long)value : 0;

    public IReadOnlyCollection<string> Keys => _values.Keys.Concat(_sets.Keys).ToArray();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];
        var key = args.Length > 0 && args[0] is RedisKey redisKey ? (string)redisKey! : null;

        switch (targetMethod.Name)
        {
            case "StringIncrementAsync" when key is not null:
            {
                var delta = args.Length > 1 && args[1] is long by ? by : 1L;
                _values[key] = Counter(key) + delta;
                return Task.FromResult((long)_values[key]);
            }
            case "KeyExpireAsync" when key is not null:
                return Task.FromResult(_values.ContainsKey(key) || _sets.ContainsKey(key));
            case "StringSetAsync" when key is not null && args.Length > 1 && args[1] is RedisValue value:
                _values[key] = value;
                return Task.FromResult(true);
            case "StringGetAsync" when key is not null:
                return Task.FromResult(_values.TryGetValue(key, out var stored) ? stored : RedisValue.Null);
            case "KeyExistsAsync" when key is not null:
                return Task.FromResult(_values.ContainsKey(key) || _sets.ContainsKey(key));
            case "KeyDeleteAsync" when key is not null:
            {
                var removed = _values.Remove(key) | _sets.Remove(key);
                return Task.FromResult(removed);
            }
            case "SetAddAsync" when key is not null && args.Length > 1 && args[1] is RedisValue member:
            {
                if (!_sets.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    _sets[key] = set;
                }
                return Task.FromResult(set.Add((string)member!));
            }
            case "SetLengthAsync" when key is not null:
                return Task.FromResult(_sets.TryGetValue(key, out var members) ? (long)members.Count : 0L);
        }

        throw new NotSupportedException($"InMemoryRedisDatabase does not implement {targetMethod}.");
    }
}
