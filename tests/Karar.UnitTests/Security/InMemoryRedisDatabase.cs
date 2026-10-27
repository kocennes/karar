using System.Reflection;
using StackExchange.Redis;

namespace Karar.UnitTests.Security;

/// Minimal in-memory IDatabase for behavioural tests of Redis-backed counters.
/// Only the commands used by the services under test are implemented; any other
/// call throws so a test never silently passes against an unimplemented command.
public class InMemoryRedisDatabase : DispatchProxy
{
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _sets = new(StringComparer.Ordinal);

    public static (IDatabase Database, InMemoryRedisDatabase State) Create()
    {
        var database = Create<IDatabase, InMemoryRedisDatabase>();
        return (database, (InMemoryRedisDatabase)(object)database);
    }

    public long Counter(string key) => _counters.TryGetValue(key, out var value) ? value : 0;

    public IReadOnlyCollection<string> Keys => _counters.Keys.Concat(_sets.Keys).ToArray();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];

        switch (targetMethod.Name)
        {
            case "StringIncrementAsync" when args[1] is long delta:
            {
                var key = (string)(RedisKey)args[0]!;
                _counters[key] = Counter(key) + delta;
                return Task.FromResult(_counters[key]);
            }
            case "KeyExpireAsync":
                return Task.FromResult(true);
            case "KeyDeleteAsync" when args[0] is RedisKey key:
            {
                var name = (string)key!;
                var removed = _counters.Remove(name) | _sets.Remove(name);
                return Task.FromResult(removed);
            }
            case "SetAddAsync" when args[1] is RedisValue value:
            {
                var key = (string)(RedisKey)args[0]!;
                if (!_sets.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    _sets[key] = set;
                }
                return Task.FromResult(set.Add((string)value!));
            }
            case "SetLengthAsync":
            {
                var key = (string)(RedisKey)args[0]!;
                return Task.FromResult(_sets.TryGetValue(key, out var set) ? (long)set.Count : 0L);
            }
        }

        throw new NotSupportedException($"InMemoryRedisDatabase does not implement {targetMethod}.");
    }
}
