using System.Net;
using System.Reflection;
using StackExchange.Redis;

namespace EclipsVault.Tests.Distributed;

internal sealed class RedisBlacklistStore
{
    private readonly Dictionary<RedisKey, RedisValue> _records = [];
    private readonly Dictionary<RedisKey, HashSet<RedisValue>> _sets = [];
    private readonly IDatabase _database;
    private readonly IServer _server;

    public IConnectionMultiplexer Connection { get; }

    /// <summary>A key whose removal runs on the server but whose reply times out.</summary>
    public RedisKey? RemovalWithoutReply { get; set; }

    public RedisBlacklistStore()
    {
        _database = Proxy<IDatabase>.Create(DatabaseCall);
        _server = Proxy<IServer>.Create(ServerCall);
        Connection = Proxy<IConnectionMultiplexer>.Create(ConnectionCall);
    }

    private object? ConnectionCall(MethodInfo method, object?[] arguments) => method.Name switch
    {
        nameof(IConnectionMultiplexer.GetDatabase) => _database,
        nameof(IConnectionMultiplexer.GetEndPoints) => new EndPoint[] { new DnsEndPoint("redis.test", 6379) },
        nameof(IConnectionMultiplexer.GetServer) => _server,
        _ => throw new NotSupportedException(method.Name)
    };

    private object? ServerCall(MethodInfo method, object?[] arguments) => method.Name switch
    {
        "get_IsConnected" => true,
        "get_IsReplica" => false,
        nameof(IServer.Keys) => _records.Keys.ToArray(),
        _ => throw new NotSupportedException(method.Name)
    };

    private object DatabaseCall(MethodInfo method, object?[] arguments)
    {
        if (arguments[0] is RedisKey[] keys && method.Name == nameof(IDatabase.KeyExistsAsync))
        {
            return Task.FromResult((long)keys.Count(candidate => _records.ContainsKey(candidate) || _sets.ContainsKey(candidate)));
        }

        var key = (RedisKey)arguments[0]!;
        switch (method.Name)
        {
            case nameof(IDatabase.SetAddAsync):
                if (!_sets.TryGetValue(key, out var members))
                {
                    _sets[key] = members = [];
                }

                return Task.FromResult(members.Add((RedisValue)arguments[1]!));
            case nameof(IDatabase.SetRemoveAsync):
                var removed = _sets.TryGetValue(key, out var held) && held.Remove((RedisValue)arguments[1]!);
                // Redis deletes a set when its last member goes.
                if (held is { Count: 0 })
                {
                    _sets.Remove(key);
                }

                return Task.FromResult(removed);
            case nameof(IDatabase.StringSetAsync):
                var when = arguments.OfType<ValueCondition>().Single();
                var added = !when.Equals(ValueCondition.NotExists) || !_records.ContainsKey(key);
                if (added)
                {
                    _records[key] = (RedisValue)arguments[1]!;
                }

                return Task.FromResult(added);
            case nameof(IDatabase.StringGetAsync):
                return Task.FromResult(_records.GetValueOrDefault(key, RedisValue.Null));
            case nameof(IDatabase.StringGetDeleteAsync):
                var deleted = _records.Remove(key, out var value) ? value : RedisValue.Null;
                return RemovalWithoutReply.Equals(key)
                    ? Task.FromException<RedisValue>(new RedisTimeoutException("Timeout awaiting response", CommandStatus.Sent))
                    : Task.FromResult(deleted);
            case nameof(IDatabase.KeyExistsAsync):
                return Task.FromResult(_records.ContainsKey(key));
            default:
                throw new NotSupportedException(method.Name);
        }
    }

    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> _call = null!;

        public static T Create(Func<MethodInfo, object?[], object?> call)
        {
            var proxy = Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy)._call = call;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => _call(targetMethod!, args!);
    }
}
