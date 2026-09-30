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

    public async Task HandleAsync(Guid eventId, CouponPlacedV1 placed)
    {
        await using var transaction = await store.BeginAsync();
        if (!await transaction.TryRecordInboxAsync(Consumer, eventId))
        {
            return;
        }

        var legs = placed.Legs.Select(l => new IndexedLeg(l.LegId, placed.CouponId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds)).ToList();
        var results = await transaction.LockResultsAsync([.. legs.Select(l => l.FixtureId).Distinct(StringComparer.Ordinal)]);
        await transaction.InsertCouponAsync(new IndexedCoupon(placed.CouponId, placed.PunterId, placed.Stake.MinorUnits, placed.Stake.Currency, legs.Count), legs);
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
