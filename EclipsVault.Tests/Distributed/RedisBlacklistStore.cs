using System.Net;
using System.Reflection;
using StackExchange.Redis;

namespace EclipsVault.Tests.Distributed;

internal sealed class RedisBlacklistStore
{
    private readonly Dictionary<RedisKey, RedisValue> _records = [];
    private readonly IDatabase _database;
    private readonly IServer _server;

    public IConnectionMultiplexer Connection { get; }

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
        var key = (RedisKey)arguments[0]!;
        switch (method.Name)
        {
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
                return Task.FromResult(_records.Remove(key, out var value) ? value : RedisValue.Null);
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
