using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Offer;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Handlers;
using SwiftBets.Settlement.Infrastructure.Persistence;
using SwiftBets.Settlement.Infrastructure.Redis;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]
[assembly: AssemblyFixture(typeof(RedisFixture))]

namespace SwiftBets.Settlement.Infrastructure.Tests;

/// <summary>Phase 2 gate scenarios on real SQL Server and Redis. Kafka is replaced by feeding each stage the events the previous one wrote to its outbox.</summary>
public sealed class SettlementFlowTests(SqlServerFixture sql, RedisFixture redis)
{
    [Fact]
    public async Task Accumulator_settles_from_results_arriving_in_any_order_and_resettles_on_a_correction()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("f1", "home", 2.00m), ("f2", "over", 1.50m));

        await flow.ResultAsync("f1", 1, ResultStatus.Official, 2, 0);
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("f2", 1, ResultStatus.Official, 2, 2);
        await flow.ResultAsync("f2", 2, ResultStatus.Correction, 1, 1);

        var settlements = await flow.SettlementsAsync(coupon.CouponId);
        settlements.ShouldBe([(1, 3_000L), (2, 0L)]);
    }

    [Fact]
    public async Task Duplicate_result_and_redelivered_evaluations_are_no_ops()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("f3", "draw", 3.00m));
        await flow.PlaceAsync(coupon);

        await flow.ResultAsync("f3", 1, ResultStatus.Official, 1, 1);
        await flow.ResultAsync("f3", 1, ResultStatus.Official, 1, 1);
        await flow.RedeliverEvaluationsAsync();

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 3_000L)]);
    }

    [Fact]
    public async Task Reconciler_repairs_a_coupon_whose_redis_progress_was_lost()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("f4", "away", 4.00m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("f4", 1, ResultStatus.Official, 0, 1, settle: false);
        await flow.LoseRedisProgressAsync(coupon.CouponId);

        var repaired = await flow.Reconciler.ReconcileAsync(TimeSpan.Zero, CancellationToken.None);

        repaired.ShouldBe(1);
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 4_000L)]);
        (await flow.OutboxCountAsync("settlement.stuck-coupon")).ShouldBe(1);
    }

    [Fact]
    public async Task Poison_free_reconciler_pass_reports_nothing_when_all_is_settled()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("f5", "under", 1.90m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("f5", 1, ResultStatus.Official, 0, 0);

        (await flow.Reconciler.ReconcileAsync(TimeSpan.Zero, CancellationToken.None)).ShouldBe(0);
        (await flow.OutboxCountAsync("settlement.stuck-coupon")).ShouldBe(0);
    }

    private sealed class Flow
    {
        private readonly string _connectionString;
        private readonly SqlSettlementStore _store;
        private readonly RedisProgressCounter _counter;
        private readonly IConnectionMultiplexer _redis;
        private int _deliveredOutbox;

        private Flow(string connectionString, SqlSettlementStore store, RedisProgressCounter counter, IConnectionMultiplexer redis)
        {
            _connectionString = connectionString;
            _store = store;
            _counter = counter;
            _redis = redis;
            Settler = new CouponSettler(store, TimeProvider.System);
            Reconciler = new ReconcileHandler(store, counter, Settler, TimeProvider.System);
        }

        public CouponSettler Settler { get; }

        public ReconcileHandler Reconciler { get; }

        public static async Task<Flow> CreateAsync(SqlServerFixture sql, RedisFixture redis)
        {
            var connectionString = await sql.CreateDatabaseAsync("settle_" + Guid.NewGuid().ToString("N")[..10]);
            var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbSettlement={connectionString}" }]);
            (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
            var kafka = Options.Create(new KafkaOptions { BootstrapServers = "unused:9092", Environment = "test", ClientId = "test" });
            var store = new SqlSettlementStore(new SqlServerConnectionFactory(connectionString), new SqlServerOutbox(kafka, TimeProvider.System), new SqlServerInboxStore(TimeProvider.System), TimeProvider.System);
            var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
            return new Flow(connectionString, store, new RedisProgressCounter(multiplexer), multiplexer);
        }

        public CouponPlacedV1 Acca(params (string Fixture, string Selection, decimal Odds)[] legs)
        {
            var id = Guid.NewGuid();
            var odds = legs.Aggregate(1m, (t, l) => t * l.Odds);
            return new CouponPlacedV1(id, Guid.NewGuid(), legs.Length == 1 ? BetType.Single : BetType.Accumulator, new Money(1_000, "ZAR"), odds, new Money((long)(1_000 * odds), "ZAR"),
                [.. legs.Select(l => new CouponLegV1(Guid.NewGuid(), Scoped(l.Fixture), $"{Scoped(l.Fixture)}-m", l.Selection, l.Odds, 1))], DateTimeOffset.UtcNow);
        }

        public async Task PlaceAsync(CouponPlacedV1 coupon)
        {
            await new IndexCouponHandler(_store, TimeProvider.System).HandleAsync(Guid.NewGuid(), coupon);
            await SettleNewEvaluationsAsync();
        }

        public async Task ResultAsync(string fixture, int version, ResultStatus status, int home, int away, bool settle = true)
        {
            await new EvaluateResultHandler(_store, new NoFaults(), TimeProvider.System)
                .HandleAsync(Guid.NewGuid(), new ResultPublishedV1(Scoped(fixture), version, status, home, away, DateTimeOffset.UtcNow));
            if (settle)
            {
                await SettleNewEvaluationsAsync();
            }
            else
            {
                _deliveredOutbox = int.MaxValue;
            }
        }

        public async Task RedeliverEvaluationsAsync()
        {
            foreach (var evaluated in await EvaluationsAsync(0))
            {
                await new SettleCouponHandler(_store, _counter, Settler, new NoFaults()).HandleAsync(evaluated);
            }
        }

        public async Task LoseRedisProgressAsync(Guid couponId) =>
            await _redis.GetDatabase().KeyDeleteAsync([RedisProgressCounter.Tokens(couponId), RedisProgressCounter.Legs(couponId)]);

        public async Task<IReadOnlyList<(int Version, long Payout)>> SettlementsAsync(Guid couponId)
        {
            await using var connection = new SqlConnection(_connectionString);
            return [.. await connection.QueryAsync<(int, long)>("SELECT Version, Payout FROM settlement.Settlements WHERE CouponId = @couponId ORDER BY Version", new { couponId })];
        }

        public async Task<int> OutboxCountAsync(string eventType)
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox.Messages WHERE EventType = @eventType", new { eventType });
        }

        private async Task SettleNewEvaluationsAsync()
        {
            foreach (var evaluated in await EvaluationsAsync(_deliveredOutbox == int.MaxValue ? 0 : _deliveredOutbox))
            {
                await new SettleCouponHandler(_store, _counter, Settler, new NoFaults()).HandleAsync(evaluated);
            }
        }

        private async Task<IReadOnlyList<LegEvaluatedV1>> EvaluationsAsync(int skip)
        {
            await using var connection = new SqlConnection(_connectionString);
            var payloads = (await connection.QueryAsync<byte[]>("SELECT Payload FROM outbox.Messages WHERE EventType = 'settlement.leg-evaluated' ORDER BY Sequence")).ToList();
            var fresh = payloads.Skip(skip).Select(p => EnvelopeSerializer.Deserialize<LegEvaluatedV1>(p)!.Payload).ToList();
            _deliveredOutbox = payloads.Count;
            return fresh;
        }

        private string Scoped(string fixture) => $"{fixture}-{_connectionString.GetHashCode():x}";

        private sealed class NoFaults : IFaultPoint
        {
            public ValueTask HitAsync(string name, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        }
    }
}
