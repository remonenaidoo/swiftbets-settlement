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
    public async Task Integrity_digest_returns_only_the_latest_settlement_of_a_resettled_coupon()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("d1", "home", 2.00m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("d1", 1, ResultStatus.Official, 2, 0);
        await flow.ResultAsync("d1", 2, ResultStatus.Correction, 0, 2);

        var digest = (await flow.Store.GetSettlementDigestAsync([coupon.CouponId], CancellationToken.None)).ShouldHaveSingleItem();

        (digest.Version, digest.Outcome, digest.Payout).ShouldBe((2, Domain.CouponOutcome.Lost, 0L));
    }

    [Fact]
    public async Task Integrity_digest_leaves_out_a_coupon_never_settled()
    {
        var flow = await Flow.CreateAsync(sql, redis);

        (await flow.Store.GetSettlementDigestAsync([Guid.NewGuid()], CancellationToken.None)).ShouldBeEmpty();
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
    public async Task A_time_void_voids_a_coupon_placed_at_or_after_the_cut_off()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var cutOff = DateTimeOffset.UtcNow.AddMinutes(-10);
        var late = flow.Acca(cutOff.AddMinutes(1), ("tv1", "home", 2.00m));
        await flow.PlaceAsync(late);

        (await flow.ManualAsync(flow.TimeVoid("tv1", cutOff))).ShouldBe(1);

        (await flow.SettlementsAsync(late.CouponId)).ShouldBe([(1, 1_000L)]);
    }

    [Fact]
    public async Task A_time_void_leaves_a_coupon_placed_before_the_cut_off_alone()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var cutOff = DateTimeOffset.UtcNow.AddMinutes(-10);
        var early = flow.Acca(cutOff.AddMinutes(-1), ("tv2", "home", 2.00m));
        await flow.PlaceAsync(early);

        (await flow.ManualAsync(flow.TimeVoid("tv2", cutOff))).ShouldBe(0);

        (await flow.SettlementsAsync(early.CouponId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_void_that_lands_before_the_coupon_is_indexed_still_voids_it()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("vb1", "home", 2.00m));

        (await flow.ManualAsync(flow.Manual(ManualResultScope.Fixture, ManualResultAction.Void, "vb1"))).ShouldBe(0);
        await flow.PlaceAsync(coupon);

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 1_000L)]);
    }

    [Fact]
    public async Task An_earlier_time_void_still_leaves_a_later_indexed_coupon_placed_before_its_cut_off_alone()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var cutOff = DateTimeOffset.UtcNow.AddMinutes(-10);
        var early = flow.Acca(cutOff.AddMinutes(-1), ("vb2", "home", 2.00m));

        await flow.ManualAsync(flow.TimeVoid("vb2", cutOff));
        await flow.PlaceAsync(early);

        (await flow.SettlementsAsync(early.CouponId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cashout_settles_at_the_agreed_amount_and_a_later_result_never_resettles_it()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("co1", "home", 2.00m), ("co2", "away", 3.00m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("co1", 1, ResultStatus.Official, 2, 0);

        var reply = await flow.CashOutAsync(coupon, 2_500);
        await flow.ResultAsync("co2", 1, ResultStatus.Official, 0, 1);

        reply.Accepted.ShouldBeTrue();
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 2_500L)]);
        (await flow.CashOutAsync(coupon, 2_500, reply.CashoutId)).WasApplied.ShouldBeFalse();
    }

    [Fact]
    public async Task A_cashout_on_a_settled_coupon_is_refused()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("cs1", "home", 2.00m));
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("cs1", 1, ResultStatus.Official, 2, 0);

        var reply = await flow.CashOutAsync(coupon, 1_500);

        reply.RefusalCode.ShouldBe("coupon_settled");
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 2_000L)]);
    }

    [Fact]
    public async Task A_trader_void_on_a_cashed_out_coupon_is_rejected_explicitly_and_changes_nothing()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("cv1", "home", 2.00m), ("cv2", "away", 3.00m));
        await flow.PlaceAsync(coupon);
        await flow.CashOutAsync(coupon, 1_800);

        (await flow.ManualAsync(flow.Manual(ManualResultScope.Fixture, ManualResultAction.Void, "cv2"))).ShouldBe(0);

        (await flow.OutboxCountAsync("trading.manual-result-rejected")).ShouldBe(1);
        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 1_800L)]);
    }

    [Fact]
    public async Task A_cashout_racing_a_late_result_pays_exactly_once()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("cr1", "home", 2.00m));
        await flow.PlaceAsync(coupon);

        var cashout = flow.CashOutAsync(coupon, 1_500);
        var result = flow.ResultAsync("cr1", 1, ResultStatus.Official, 2, 0);
        await Task.WhenAll(cashout, result);

        // Whichever committed first decided; there is exactly one settlement either way.
        var settlements = await flow.SettlementsAsync(coupon.CouponId);
        settlements.ShouldHaveSingleItem();
        settlements[0].Payout.ShouldBe((await cashout).Accepted ? 1_500L : 2_000L);
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
    public async Task A_tennis_total_games_and_a_cricket_top_batter_settle_from_the_result_detail()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = Flow.On(flow.Acca(("t1", "over:22.5", 1.90m), ("c1", "player-jos-buttler", 4.00m)), "games", "topbat");
        await flow.PlaceAsync(coupon);

        await flow.ResultAsync("t1", 1, ResultStatus.Official, 2, 1, detail: new ResultDetailV1(13, 11));
        await flow.ResultAsync("c1", 1, ResultStatus.Official, 170, 150, detail: new ResultDetailV1(Winners: ["player-jos-buttler"]));

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 7_600L)]);
    }

    [Fact]
    public async Task A_rugby_handicap_on_a_whole_line_that_lands_on_it_is_void_and_returns_the_stake()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = Flow.On(flow.Acca(("r1", "home:-7.0", 1.90m)), "hcp");
        await flow.PlaceAsync(coupon);

        await flow.ResultAsync("r1", 1, ResultStatus.Official, 27, 20);

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 1_000L)]);
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
        (await flow.OutboxCountAsync("settlement.coupon-settled")).ShouldBe(2);
    }

    [Fact]
    public async Task A_coupon_delivered_twice_is_indexed_once_and_keeps_placements_bet_id()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("d1", "home", 2.00m), ("d2", "away", 3.00m));

        await flow.PlaceAsync(coupon);
        await flow.PlaceAsync(coupon);
        await flow.ResultAsync("d1", 1, ResultStatus.Official, 1, 0);
        await flow.ResultAsync("d2", 1, ResultStatus.Official, 0, 2);

        (await flow.SettlementsAsync(coupon.CouponId)).ShouldBe([(1, 6_000L)]);
        (await flow.SettledV2Async()).Single().Bets.Single().BetId.ShouldBe(coupon.Bets[0].BetId);
    }

    [Fact]
    public async Task A_settlement_is_published_as_v2_only()
    {
        var flow = await Flow.CreateAsync(sql, redis);
        var coupon = flow.Acca(("o1", "home", 2.00m));
        await flow.PlaceAsync(coupon);

        await flow.ResultAsync("o1", 1, ResultStatus.Official, 1, 0);

        (await flow.OutboxTopicCountAsync("%.coupon-settled.v2.%")).ShouldBe(1);
        (await flow.OutboxTopicCountAsync("%.coupon-settled.v1.%")).ShouldBe(0);
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

        public SqlSettlementStore Store => _store;

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

        public CouponPlacedV2 Acca(params (string Fixture, string Selection, decimal Odds)[] legs) => Acca(DateTimeOffset.UtcNow, legs);

        /// <summary>One accumulator bet over every leg at R10, as placement publishes a single or an acca.</summary>
        public CouponPlacedV2 Acca(DateTimeOffset placedAt, params (string Fixture, string Selection, decimal Odds)[] legs)
        {
            var stake = new Money(1_000, "ZAR");
            var payout = new Money((long)(1_000 * legs.Aggregate(1m, (t, l) => t * l.Odds)), "ZAR");
            return new CouponPlacedV2(Guid.NewGuid(), Guid.NewGuid(), stake, payout,
                [.. legs.Select(l => new CouponLegV2(Guid.NewGuid(), Scoped(l.Fixture), $"{Scoped(l.Fixture)}-m", l.Selection, l.Odds, 1, false))],
                [new CouponBetV2(Guid.NewGuid(), legs.Length == 1 ? "single" : "accumulator", [legs.Length], 1, stake, stake, payout)], placedAt);
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

        /// <summary>The coupon with each leg on a market of the given type suffix, in leg order.</summary>
        public static CouponPlacedV2 On(CouponPlacedV2 coupon, params string[] suffixes) =>
            coupon with { Legs = [.. coupon.Legs.Select((l, i) => l with { MarketId = $"{l.FixtureId}-{suffixes[i]}" })] };

        public async Task ResultAsync(string fixture, int version, ResultStatus status, int home, int away, bool settle = true, ResultDetailV1? detail = null)
        {
            await new EvaluateResultHandler(_store, new NoFaults(), TimeProvider.System)
                .HandleAsync(Guid.NewGuid(), new ResultPublishedV1(Scoped(fixture), version, status, home, away, DateTimeOffset.UtcNow, detail));
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

        public ManualResultV1 TimeVoid(string fixture, DateTimeOffset voidFrom) =>
            new(Guid.NewGuid(), ManualResultScope.Fixture, ManualResultAction.TimeVoid, Scoped(fixture), null, null, null, voidFrom,
                "late bets after a goal", Guid.NewGuid(), DateTimeOffset.UtcNow);

        public async Task<(bool Accepted, string? RefusalCode, bool WasApplied, Guid CashoutId)> CashOutAsync(CouponPlacedV2 coupon, long amount, Guid? cashoutId = null)
        {
            var id = cashoutId ?? Guid.NewGuid();
            var reply = await new CashOutHandler(_store, _counter, TimeProvider.System)
                .HandleAsync(new CashOutHandler.Request(id, coupon.CouponId, coupon.PunterId, amount, coupon.TotalStake.Currency));
            return (reply.Accepted, reply.RefusalCode, reply.WasApplied, id);
        }

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

        public async Task<int> OutboxTopicCountAsync(string topicPattern)
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM outbox.Messages WHERE Topic LIKE @TopicPattern", new { TopicPattern = topicPattern });
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
