using SwiftBets.Contracts.Messaging;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Ports;

/// <summary>One SQL transaction: every read, write, inbox mark and outbox event inside it commits together.</summary>
public interface ISettlementTransaction : IAsyncDisposable
{
    Task<bool> TryRecordInboxAsync(string consumer, Guid eventId);

    /// <summary>Indexes a coupon with its legs and bets; false when it is already indexed (it was published as V1 and V2).</summary>
    /// <summary>Legs on a fixture with their coupon's placement time, for time-voids.</summary>
    Task<IReadOnlyList<TimedLeg>> GetTimedLegsForFixtureAsync(string fixtureId);

    /// <summary>The coupon's final state, read under the coupon lock taken by <see cref="LockCouponAsync"/>.</summary>
    Task<Domain.FinalState?> GetFinalStateAsync(Guid couponId);

    Task MarkFinalAsync(Guid couponId, Domain.FinalState state);

    Task<CashoutRecord?> GetCashoutAsync(Guid cashoutId);

    Task InsertCashoutAsync(CashoutRecord cashout);

    /// <summary>Every leg of the coupon in position order, with its latest evaluation.</summary>
    Task<IReadOnlyList<CouponLeg>> GetCouponLegsAsync(Guid couponId);

    Task<bool> TryInsertCouponAsync(IndexedCoupon coupon, IReadOnlyList<IndexedLeg> legs, IReadOnlyList<SettlementBet> bets);

    Task<IReadOnlyList<SettlementBet>> GetBetsAsync(Guid couponId);

    /// <summary>A coupon indexed from V1 takes placement's bet id when its V2 twin arrives; nothing else changes.</summary>
    Task AdoptBetIdAsync(Guid couponId, SettlementBet bet);

    /// <summary>Reads results for the fixtures under lock, so a result landing concurrently is either seen here or sees these legs.</summary>
    Task<IReadOnlyDictionary<string, FixtureResult>> LockResultsAsync(IReadOnlyList<string> fixtureIds);

    Task SaveManualResultAsync(StoredManualResult manual);

    /// <summary>Manual results on these fixtures, oldest version first.</summary>
    Task<IReadOnlyList<StoredManualResult>> GetManualResultsAsync(IReadOnlyList<string> fixtureIds);

    Task<FixtureResult?> LockResultAsync(string fixtureId);

    Task SaveResultAsync(FixtureResult result);

    Task<IReadOnlyList<IndexedLeg>> GetLegsForFixtureAsync(string fixtureId);

    Task<bool> TryInsertEvaluationAsync(IndexedLeg leg, int resultVersion, LegOutcome outcome);

    Task<IndexedCoupon?> LockCouponAsync(Guid couponId);

    Task<IReadOnlyList<LegEvaluation>> GetLatestEvaluationsAsync(Guid couponId);

    Task<StoredSettlement?> GetLatestSettlementAsync(Guid couponId);

    /// <summary>Writes the settlement and clears the coupon's pending flag.</summary>
    Task InsertSettlementAsync(Guid couponId, int version, CouponSettlement settlement);

    Task ClearPendingAsync(Guid couponId);

    Task EnqueueAsync<TPayload>(string topicBase, string key, TPayload payload)
        where TPayload : IEventContract;

    Task CommitAsync();
}
