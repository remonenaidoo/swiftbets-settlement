using StackExchange.Redis;
using SwiftBets.Settlement.Application.Ports;

namespace SwiftBets.Settlement.Infrastructure.Redis;

public sealed class RedisProgressCounter(IConnectionMultiplexer redis) : IProgressCounter
{
    private static readonly long TtlSeconds = (long)TimeSpan.FromDays(3).TotalSeconds;
    private static readonly LuaScript Record = LuaScript.Prepare(Read("RecordProgress.lua"));
    private static readonly LuaScript Rebuild = LuaScript.Prepare(Read("RebuildProgress.lua"));

    public static string Tokens(Guid couponId) => $"settle:{couponId:N}:tokens";

    public static string Legs(Guid couponId) => $"settle:{couponId:N}:legs";

    public async Task<(bool Applied, int ResolvedLegs)> RecordAsync(Guid couponId, Guid legId, int resultVersion)
    {
        var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
            Record.ExecutableScript, [Tokens(couponId), Legs(couponId)], [$"{legId:N}:{resultVersion}", legId.ToString("N"), resultVersion, TtlSeconds]))!;
        return ((long)result[0] == 1, (int)(long)result[1]);
    }

    public async Task<int> ResolvedLegsAsync(Guid couponId) => (int)await redis.GetDatabase().HashLengthAsync(Legs(couponId));

    public async Task RebuildAsync(Guid couponId, IReadOnlyList<LegEvaluation> evaluations)
    {
        var args = new List<RedisValue> { TtlSeconds };
        foreach (var evaluation in evaluations)
        {
            args.Add(evaluation.LegId.ToString("N"));
            args.Add(evaluation.ResultVersion);
        }

        await redis.GetDatabase().ScriptEvaluateAsync(Rebuild.ExecutableScript, [Tokens(couponId), Legs(couponId)], [.. args]);
    }

    private static string Read(string name)
    {
        using var stream = typeof(RedisProgressCounter).Assembly.GetManifestResourceStream($"SwiftBets.Settlement.Infrastructure.Redis.{name}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
