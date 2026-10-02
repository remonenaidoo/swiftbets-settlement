using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Infrastructure.Persistence;

internal sealed class SqlSettlementTransaction(SqlConnection connection, SqlTransaction transaction, IOutbox outbox, IInboxStore inbox, TimeProvider time) : ISettlementTransaction
{
    private static readonly SqlResources Sql = SqlResources.For<SqlSettlementTransaction>();

    public Task<bool> TryRecordInboxAsync(string consumer, Guid eventId) => inbox.TryRecordAsync(transaction, consumer, eventId, CancellationToken.None);

    public async Task<bool> TryInsertCouponAsync(IndexedCoupon coupon, IReadOnlyList<IndexedLeg> legs, IReadOnlyList<SettlementBet> bets)
    {
        if (await connection.ExecuteAsync(Sql.Get("Settle.InsertCoupon"), new { coupon.CouponId, coupon.PunterId, coupon.Stake, coupon.Currency, coupon.LegCount, Now = time.GetUtcNow(), coupon.PlacedAt }, transaction) != 1)
        {
            return false;
        }

        await connection.ExecuteAsync(Sql.Get("Settle.InsertLeg"), legs, transaction);
        await connection.ExecuteAsync(Sql.Get("Settle.InsertBet"), bets.Select(b => new
        {
            BetId = b.BetId == Guid.Empty ? Guid.CreateVersion7() : b.BetId, coupon.CouponId, Folds = string.Join(',', b.Folds), b.UnitStake,
        }), transaction);
        return true;
    }

    public Task AdoptBetIdAsync(Guid couponId, SettlementBet bet) =>
        connection.ExecuteAsync(Sql.Get("Settle.AdoptBetId"), new { CouponId = couponId, bet.BetId, Folds = string.Join(',', bet.Folds), bet.UnitStake }, transaction);

    public async Task<IReadOnlyList<SettlementBet>> GetBetsAsync(Guid couponId) =>
        [.. (await connection.QueryAsync<(Guid BetId, string Folds, long UnitStake)>(Sql.Get("Settle.BetsForCoupon"), new { CouponId = couponId }, transaction))
            .Select(b => new SettlementBet(b.BetId, [.. b.Folds.Split(',').Select(f => int.Parse(f, System.Globalization.CultureInfo.InvariantCulture))], b.UnitStake))];

    public async Task<IReadOnlyDictionary<string, FixtureResult>> LockResultsAsync(IReadOnlyList<string> fixtureIds) =>
        (await connection.QueryAsync<ResultRow>(Sql.Get("Settle.LockResults"), new { FixtureIds = fixtureIds }, transaction))
            .ToDictionary(r => r.FixtureId, r => r.ToDomain(), StringComparer.Ordinal);

    public Task SaveManualResultAsync(StoredManualResult manual) =>
        connection.ExecuteAsync(Sql.Get("Settle.SaveManualResult"), new
        {
            manual.ManualResultId, manual.FixtureId, Scope = (byte)manual.Scope, Action = (byte)manual.Action, manual.MarketId, manual.CouponId,
            manual.WinningSelectionId, manual.VoidFrom, manual.Version, manual.IssuedAt,
        }, transaction);

    public async Task<IReadOnlyList<StoredManualResult>> GetManualResultsAsync(IReadOnlyList<string> fixtureIds) =>
        [.. (await connection.QueryAsync<(Guid ManualResultId, string FixtureId, byte Scope, byte Action, string? MarketId, Guid? CouponId, string? WinningSelectionId, DateTimeOffset? VoidFrom, int Version, DateTimeOffset IssuedAt)>(
            Sql.Get("Settle.ManualResultsForFixtures"), new { FixtureIds = fixtureIds }, transaction))
            .Select(r => new StoredManualResult(r.ManualResultId, r.FixtureId, (SwiftBets.Contracts.Trading.ManualResultScope)r.Scope, (SwiftBets.Contracts.Trading.ManualResultAction)r.Action,
                r.MarketId, r.CouponId, r.WinningSelectionId, r.VoidFrom, r.Version, r.IssuedAt))];

    public async Task<FixtureResult?> LockResultAsync(string fixtureId) =>
        (await connection.QuerySingleOrDefaultAsync<ResultRow>(Sql.Get("Settle.LockResult"), new { FixtureId = fixtureId }, transaction))?.ToDomain();

    public Task SaveResultAsync(FixtureResult result) =>
        connection.ExecuteAsync(Sql.Get("Settle.SaveResult"), new { result.FixtureId, result.Version, State = (byte)result.State, result.HomeGoals, result.AwayGoals, Now = time.GetUtcNow() }, transaction);

    public async Task<IReadOnlyList<IndexedLeg>> GetLegsForFixtureAsync(string fixtureId) =>
        [.. await connection.QueryAsync<IndexedLeg>(Sql.Get("Settle.LegsForFixture"), new { FixtureId = fixtureId }, transaction)];

    public async Task<IReadOnlyList<TimedLeg>> GetTimedLegsForFixtureAsync(string fixtureId) =>
        [.. (await connection.QueryAsync<(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds, bool IsBanker, int Position, DateTimeOffset? PlacedAt, byte? FinalState)>(
            Sql.Get("Settle.TimedLegsForFixture"), new { FixtureId = fixtureId }, transaction))
            .Select(r => new TimedLeg(new IndexedLeg(r.LegId, r.CouponId, r.FixtureId, r.MarketId, r.SelectionId, r.Odds, r.IsBanker, r.Position), r.PlacedAt, (FinalState?)r.FinalState))];

    public async Task<FinalState?> GetFinalStateAsync(Guid couponId) =>
        (FinalState?)await connection.ExecuteScalarAsync<byte?>(Sql.Get("Settle.FinalState"), new { CouponId = couponId }, transaction);

    public Task MarkFinalAsync(Guid couponId, FinalState state) =>
        connection.ExecuteAsync(Sql.Get("Settle.MarkFinal"), new { CouponId = couponId, FinalState = (byte)state }, transaction);

    public Task<CashoutRecord?> GetCashoutAsync(Guid cashoutId) =>
        connection.QuerySingleOrDefaultAsync<CashoutRecord>(Sql.Get("Settle.GetCashout"), new { CashoutId = cashoutId }, transaction);

    public Task InsertCashoutAsync(CashoutRecord cashout) =>
        connection.ExecuteAsync(Sql.Get("Settle.InsertCashout"), cashout, transaction);

    public async Task<IReadOnlyList<CouponLeg>> GetCouponLegsAsync(Guid couponId) =>
        [.. (await connection.QueryAsync<(Guid LegId, string FixtureId, string MarketId, string SelectionId, decimal Odds, bool IsBanker, int Position, byte? Outcome)>(
            Sql.Get("Settle.CouponLegs"), new { CouponId = couponId }, transaction))
            .Select(r => new CouponLeg(r.LegId, r.FixtureId, r.MarketId, r.SelectionId, r.Odds, r.IsBanker, r.Position, (LegOutcome?)r.Outcome))];

    public async Task<bool> TryInsertEvaluationAsync(IndexedLeg leg, int resultVersion, LegOutcome outcome) =>
        await connection.ExecuteScalarAsync<int>(Sql.Get("Settle.InsertEvaluation"),
            new { leg.LegId, ResultVersion = resultVersion, leg.CouponId, Outcome = (byte)outcome, Now = time.GetUtcNow() }, transaction) == 1;

    public Task<IndexedCoupon?> LockCouponAsync(Guid couponId) =>
        connection.QuerySingleOrDefaultAsync<IndexedCoupon>(Sql.Get("Settle.LockCoupon"), new { CouponId = couponId }, transaction);

    public async Task<IReadOnlyList<LegEvaluation>> GetLatestEvaluationsAsync(Guid couponId) =>
        [.. (await connection.QueryAsync<(Guid LegId, int ResultVersion, byte Outcome, decimal Odds, bool IsBanker, int Position)>(Sql.Get("Settle.LatestEvaluations"), new { CouponId = couponId }, transaction))
            .Select(r => new LegEvaluation(r.LegId, r.ResultVersion, (LegOutcome)r.Outcome, r.Odds, r.IsBanker, r.Position))];

    public async Task<StoredSettlement?> GetLatestSettlementAsync(Guid couponId) =>
        (await connection.QuerySingleOrDefaultAsync<(int Version, byte Outcome, long Payout)?>(Sql.Get("Settle.LatestSettlement"), new { CouponId = couponId }, transaction)) is { } row
            ? new StoredSettlement(row.Version, (CouponOutcome)row.Outcome, row.Payout)
            : null;

    public Task InsertSettlementAsync(Guid couponId, int version, CouponSettlement settlement) =>
        connection.ExecuteAsync(Sql.Get("Settle.InsertSettlement"),
            new { CouponId = couponId, Version = version, Outcome = (byte)settlement.Outcome, settlement.EffectiveOdds, settlement.Payout, Now = time.GetUtcNow() }, transaction);

    public Task ClearPendingAsync(Guid couponId) => connection.ExecuteAsync(Sql.Get("Settle.ClearPending"), new { CouponId = couponId }, transaction);

    public Task EnqueueAsync<TPayload>(string topicBase, string key, TPayload payload)
        where TPayload : IEventContract =>
        outbox.EnqueueAsync(transaction, topicBase, key, EventEnvelope<TPayload>.Create(payload, time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId()), CancellationToken.None);

    public Task CommitAsync() => transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    internal sealed record ResultRow(string FixtureId, int Version, byte State, int HomeGoals, int AwayGoals)
    {
        public FixtureResult ToDomain() => new(FixtureId, Version, (ResultState)State, HomeGoals, AwayGoals);
    }
}
