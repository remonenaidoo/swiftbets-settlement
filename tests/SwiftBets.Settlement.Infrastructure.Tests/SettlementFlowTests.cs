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
using SwiftBets.Contracts.Trading;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task A_trader_voids_a_market_after_the_feed_settled_it_and_a_later_feed_correction_cannot_undo_it()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("m1", "home", 2.00m), ("m2", "away", 3.00m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("m1", 1, ResultStatus.Official, 2, 0);
        await flow.ResultAsync("m2", 1, ResultStatus.Official, 0, 1);

        var voidMarket = flow.Manual(ManualResultScope.Market, ManualResultAction.Void, "m2");
        (await flow.ManualAsync(voidMarket)).ShouldBe(1);
        (await flow.ManualAsync(voidMarket)).ShouldBe(0);
        await flow.ResultAsync("m2", 2, ResultStatus.Correction, 0, 2);

        // Won at 6.00, then the void leaves only the home leg at 2.00; the correction changes nothing.
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 6_000L), (2, 2_000L)]);
    }

    [Fact]
    public async Task A_trader_settles_a_market_the_feed_never_resulted()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("s1", "draw", 3.00m));
        await flow.PlaceAsync(coupon);

        await flow.ManualAsync(flow.Manual(ManualResultScope.Fixture, ManualResultAction.Settle, "s1", "draw"));

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 3_000L)]);
    }

    [Fact]
    public async Task A_banker_trixie_settles_from_out_of_order_results_and_resettles_on_a_correction()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.BankerTrixie(("b1", "home", 2.00m), ("t2", "home", 2.00m), ("t3", "draw", 3.00m), ("t4", "over", 1.50m));

        await flow.ResultAsync("t3", 1, ResultStatus.Official, 1, 1);
        await flow.ResultAsync("b1", 1, ResultStatus.Official, 2, 0);
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("t4", 1, ResultStatus.Official, 3, 1);
        await flow.ResultAsync("t2", 1, ResultStatus.Official, 0, 1);
        await flow.ResultAsync("t2", 2, ResultStatus.Correction, 2, 0);

        // Banker x (t3, t4) only, then every line once t2 is corrected to a home win.
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 900L), (2, 1_200L + 600L + 900L + 1_800L)]);
        var latest = (await flow.SettledV2Async())[^1];
        latest.Bets.Single().ShouldBe(new BetSettlementV2(coupon.Bets[0].BetId, CouponOutcome.Won, 4, 0, 0, new Money(4_500, "ZAR")));
        (await flow.OutboxCountAsync("settlement.coupon-settled")).ShouldBe(4);
    }

    [Fact]
    public async Task A_coupon_published_as_v1_and_v2_is_indexed_once_and_takes_placements_bet_id()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var v1 = flow.Acca(("d1", "home", 2.00m), ("d2", "away", 3.00m));
        var betId = Guid.NewGuid();
        var v2 = new CouponPlacedV2(v1.CouponId, v1.PunterId, v1.Stake, v1.PotentialPayout,
            [.. v1.Legs.Select(l => new CouponLegV2(l.LegId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds, l.OfferVersion, false))],
            [new CouponBetV2(betId, "accumulator", [2], 1, v1.Stake, v1.Stake, v1.PotentialPayout)], v1.PlacedAt);

        await flow.PlaceAsync(v1);
        await flow.PlaceAsync(v2);
        await flow.ResultAsync("d1", 1, ResultStatus.Official, 1, 0);
        await flow.ResultAsync("d2", 1, ResultStatus.Official, 0, 2);

        (await flow.SettlementsAsync(v1.CouponId)).ShouldBe([(1, 6_000L)]);
        (await flow.SettledV2Async()).Single().Bets.Single().BetId.ShouldBe(betId);
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

        /// <summary>The first leg is a banker; the rest make a Trixie at R1 a line.</summary>
        public CouponPlacedV2 BankerTrixie(params (string Fixture, string Selection, decimal Odds)[] legs) =>
            new(Guid.NewGuid(), Guid.NewGuid(), new Money(400, "ZAR"), new Money(10_000, "ZAR"),
                [.. legs.Select((l, i) => new CouponLegV2(Guid.NewGuid(), Scoped(l.Fixture), $"{Scoped(l.Fixture)}-m", l.Selection, l.Odds, 1, i == 0))],
                [new CouponBetV2(Guid.NewGuid(), "trixie", [2, 3], 4, new Money(100, "ZAR"), new Money(400, "ZAR"), new Money(10_000, "ZAR"))], DateTimeOffset.UtcNow);

        public async Task PlaceAsync(CouponPlacedV2 coupon)
        {
            await new IndexCouponHandler(_store, TimeProvider.System).HandleAsync(Guid.NewGuid(), coupon);
            await SettleNewEvaluationsAsync();
        }

        public async Task<IReadOnlyList<CouponSettledV2>> SettledV2Async()
        {
            await using var connection = new SqlConnection(_connectionString);
            var payloads = await connection.QueryAsync<byte[]>("SELECT Payload FROM outbox.Messages WHERE EventType = 'settlement.coupon-settled' AND Topic LIKE '%.coupon-settled.v2.%' ORDER BY Sequence");
            return [.. payloads.Select(p => EnvelopeSerializer.Deserialize<CouponSettledV2>(p)!.Payload)];
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

        public ManualResultV1 Manual(ManualResultScope scope, ManualResultAction action, string fixture, string? winner = null) =>
            new(Guid.NewGuid(), scope, action, Scoped(fixture), scope == ManualResultScope.Market ? $"{Scoped(fixture)}-m" : null, null, winner, null,
                "trader decision", Guid.NewGuid(), DateTimeOffset.UtcNow);

        public async Task<int> ManualAsync(ManualResultV1 manual)
        {
            var evaluated = await new ApplyManualResultHandler(_store, TimeProvider.System, NullLogger<ApplyManualResultHandler>.Instance).HandleAsync(manual);
            await SettleNewEvaluationsAsync();
            return evaluated;
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
