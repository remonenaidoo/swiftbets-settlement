using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>Indexes a placed coupon's legs by fixture. A leg whose result already landed is evaluated in the same transaction.</summary>
public sealed class IndexCouponHandler(ISettlementStore store, TimeProvider time)
{
    public const string Consumer = "settlement.indexer";
    public const string ConsumerV2 = "settlement.indexer.v2";

    /// <summary>A V1 coupon is one bet: every leg in one line, the whole stake on it.</summary>
    public Task HandleAsync(Guid eventId, CouponPlacedV1 placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        var legs = placed.Legs.Select((l, i) => new IndexedLeg(l.LegId, placed.CouponId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds, false, i)).ToList();
        return IndexAsync(Consumer, eventId, new IndexedCoupon(placed.CouponId, placed.PunterId, placed.Stake.MinorUnits, placed.Stake.Currency, legs.Count, placed.PlacedAt),
            legs, [new SettlementBet(Guid.Empty, [legs.Count], placed.Stake.MinorUnits)]);
    }

    public Task HandleAsync(Guid eventId, CouponPlacedV2 placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        var legs = placed.Legs.Select((l, i) => new IndexedLeg(l.LegId, placed.CouponId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds, l.IsBanker, i)).ToList();
        return IndexAsync(ConsumerV2, eventId, new IndexedCoupon(placed.CouponId, placed.PunterId, placed.TotalStake.MinorUnits, placed.TotalStake.Currency, legs.Count, placed.PlacedAt),
            legs, [.. placed.Bets.Select(b => new SettlementBet(b.BetId, b.Folds, b.UnitStake.MinorUnits))]);
    }

    private async Task IndexAsync(string consumer, Guid eventId, IndexedCoupon coupon, List<IndexedLeg> legs, IReadOnlyList<SettlementBet> bets)
    {
        await using var transaction = await store.BeginAsync();
        if (!await transaction.TryRecordInboxAsync(consumer, eventId))
        {
            return;
        }

        var results = await transaction.LockResultsAsync([.. legs.Select(l => l.FixtureId).Distinct(StringComparer.Ordinal)]);
        if (!await transaction.TryInsertCouponAsync(coupon, legs, bets))
        {
            if (bets is [{ BetId: var id } bet] && id != Guid.Empty)
            {
                await transaction.AdoptBetIdAsync(coupon.CouponId, bet);
            }

            await transaction.CommitAsync();
            return;
        }

        foreach (var leg in legs)
        {
            if (results.TryGetValue(leg.FixtureId, out var result) && result.IsSettleable)
            {
                var outcome = LegRules.Evaluate(leg.SelectionId, result);
                await transaction.TryInsertEvaluationAsync(leg, result.Version, outcome);
                await transaction.EnqueueAsync(Topics.LegEvaluated, leg.CouponId.ToString(),
                    new LegEvaluatedV1(leg.CouponId, leg.LegId, leg.FixtureId, result.Version, SettlementMapping.Map(outcome), time.GetUtcNow()));
            }
        }

        await transaction.CommitAsync();
    }
}
