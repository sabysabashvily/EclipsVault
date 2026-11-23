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

    public Func<Task>? AfterStuckMemberRemoval { get; set; }

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
        // Patterns here are a prefix and a trailing *.
        nameof(IServer.Keys) => _records.Keys.Concat(_sets.Keys)
            .Where(key => key.ToString().StartsWith(arguments[1]!.ToString()!.TrimEnd('*'), StringComparison.Ordinal))
            .ToArray(),
        _ => throw new NotSupportedException(method.Name)
    };

    private object DatabaseCall(MethodInfo method, object?[] arguments)
    {
        if (method.Name == nameof(IDatabase.ScriptEvaluateAsync))
        {
            return Task.FromResult(EvaluateScript((string)arguments[0]!, (RedisKey[])arguments[1]!, (RedisValue[]?)arguments[2]));
        }

        if (method.Name == nameof(IDatabase.SetCombineAsync) && arguments[0] is SetOperation.Intersect)
        {
            return Task.FromResult(Intersect((RedisKey)arguments[1]!, (RedisKey)arguments[2]!));
        }

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

                return arguments[1] is RedisValue[] adding
                    ? Task.FromResult((long)adding.Count(members.Add))
                    : Task.FromResult(members.Add((RedisValue)arguments[1]!));
            case nameof(IDatabase.SetRemoveAsync):
                _sets.TryGetValue(key, out var held);
                var batch = arguments[1] as RedisValue[];
                var removing = batch ?? [(RedisValue)arguments[1]!];
                var removed = removing.Count(member => held?.Remove(member) == true);
                // Redis deletes a set when its last member goes.
                if (held is { Count: 0 })
                {
                    _sets.Remove(key);
                }

                if (batch is null && key.ToString().Contains(":ipstuck:", StringComparison.Ordinal)
                    && AfterStuckMemberRemoval is { } afterRemoval)
                {
                    AfterStuckMemberRemoval = null;
                    return ReplyAfterAsync(afterRemoval, removed == 1);
                }

                if (batch is not null && RemovalWithoutReply.Equals(key))
                {
                    return Task.FromException<long>(new RedisTimeoutException("Timeout awaiting response", CommandStatus.Sent));
                }

                return batch is null ? Task.FromResult(removed == 1) : Task.FromResult((long)removed);
            case nameof(IDatabase.SetMembersAsync):
                return Task.FromResult(_sets.TryGetValue(key, out var listed) ? listed.ToArray() : Array.Empty<RedisValue>());
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

    private RedisResult EvaluateScript(string script, RedisKey[] keys, RedisValue[]? arguments)
    {
        var stuck = keys[0];
        var guard = keys[1];
        if (script.StartsWith("local claimed = redis.call('SINTER'", StringComparison.Ordinal))
        {
            var claimed = Intersect(stuck, guard);
            _sets.Remove(stuck);
            return RedisResult.Create(claimed);
        }

        if (script.StartsWith("for _, member in ipairs(ARGV)", StringComparison.Ordinal))
        {
            foreach (var member in arguments!)
            {
                if (_sets.TryGetValue(guard, out var guarded) && guarded.Contains(member))
                {
                    if (!_sets.TryGetValue(stuck, out var marked))
                    {
                        _sets[stuck] = marked = [];
                    }

                    marked.Add(member);
                }
            }

            return RedisResult.Create(1);
        }

        throw new NotSupportedException(script);
    }

    private RedisValue[] Intersect(RedisKey first, RedisKey second)
        => _sets.TryGetValue(first, out var left) && _sets.TryGetValue(second, out var right)
            ? left.Intersect(right).ToArray()
            : [];

    private static async Task<bool> ReplyAfterAsync(Func<Task> afterRemoval, bool removed)
    {
        await afterRemoval();
        return removed;
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
