using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>Indexes a placed coupon's legs by fixture. A leg whose result already landed is evaluated in the same transaction.</summary>
public sealed class IndexCouponHandler(ISettlementStore store, TimeProvider time)
{
    public const string ConsumerV2 = "settlement.indexer.v2";

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
                await EvaluateAsync(transaction, leg, result.Version, LegRules.Evaluate(leg.MarketId, leg.SelectionId, result));
            }
        }

        // A trader's result that landed before this coupon's placement event still applies to it, above any feed version.
        var manuals = await transaction.GetManualResultsAsync([.. legs.Select(l => l.FixtureId).Distinct(StringComparer.Ordinal)]);
        foreach (var manual in manuals.Where(m => ManualResultRules.AppliesTo(m, coupon.PlacedAt)))
        {
            foreach (var leg in legs.Where(l => ManualResultRules.InScope(manual, l)))
            {
                await EvaluateAsync(transaction, leg, manual.Version, ManualResultRules.Outcome(manual, leg));
            }
        }

        await transaction.CommitAsync();
    }

    private async Task EvaluateAsync(ISettlementTransaction transaction, IndexedLeg leg, int version, Domain.LegOutcome outcome)
    {
        if (await transaction.TryInsertEvaluationAsync(leg, version, outcome))
        {
            await transaction.EnqueueAsync(Topics.LegEvaluated, leg.CouponId.ToString(),
                new LegEvaluatedV1(leg.CouponId, leg.LegId, leg.FixtureId, version, SettlementMapping.Map(outcome), time.GetUtcNow()));
        }
    }
}
