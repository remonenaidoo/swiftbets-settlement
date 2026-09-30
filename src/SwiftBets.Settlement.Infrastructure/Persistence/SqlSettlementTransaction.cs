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

    public async Task InsertCouponAsync(IndexedCoupon coupon, IReadOnlyList<IndexedLeg> legs)
    {
        await connection.ExecuteAsync(Sql.Get("Settle.InsertCoupon"), new { coupon.CouponId, coupon.PunterId, coupon.Stake, coupon.Currency, coupon.LegCount, Now = time.GetUtcNow() }, transaction);
        await connection.ExecuteAsync(Sql.Get("Settle.InsertLeg"), legs, transaction);
    }

    public async Task<IReadOnlyDictionary<string, FixtureResult>> LockResultsAsync(IReadOnlyList<string> fixtureIds) =>
        (await connection.QueryAsync<ResultRow>(Sql.Get("Settle.LockResults"), new { FixtureIds = fixtureIds }, transaction))
            .ToDictionary(r => r.FixtureId, r => r.ToDomain(), StringComparer.Ordinal);

    public async Task<FixtureResult?> LockResultAsync(string fixtureId) =>
        (await connection.QuerySingleOrDefaultAsync<ResultRow>(Sql.Get("Settle.LockResult"), new { FixtureId = fixtureId }, transaction))?.ToDomain();

    public Task SaveResultAsync(FixtureResult result) =>
        connection.ExecuteAsync(Sql.Get("Settle.SaveResult"), new { result.FixtureId, result.Version, State = (byte)result.State, result.HomeGoals, result.AwayGoals, Now = time.GetUtcNow() }, transaction);

    public async Task<IReadOnlyList<IndexedLeg>> GetLegsForFixtureAsync(string fixtureId) =>
        [.. await connection.QueryAsync<IndexedLeg>(Sql.Get("Settle.LegsForFixture"), new { FixtureId = fixtureId }, transaction)];

    public async Task<bool> TryInsertEvaluationAsync(IndexedLeg leg, int resultVersion, LegOutcome outcome) =>
        await connection.ExecuteScalarAsync<int>(Sql.Get("Settle.InsertEvaluation"),
            new { leg.LegId, ResultVersion = resultVersion, leg.CouponId, Outcome = (byte)outcome, Now = time.GetUtcNow() }, transaction) == 1;

    public Task<IndexedCoupon?> LockCouponAsync(Guid couponId) =>
        connection.QuerySingleOrDefaultAsync<IndexedCoupon>(Sql.Get("Settle.LockCoupon"), new { CouponId = couponId }, transaction);

    public async Task<IReadOnlyList<LegEvaluation>> GetLatestEvaluationsAsync(Guid couponId) =>
        [.. (await connection.QueryAsync<(Guid LegId, int ResultVersion, byte Outcome, decimal Odds)>(Sql.Get("Settle.LatestEvaluations"), new { CouponId = couponId }, transaction))
            .Select(r => new LegEvaluation(r.LegId, r.ResultVersion, (LegOutcome)r.Outcome, r.Odds))];

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
