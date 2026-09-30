namespace SwiftBets.Settlement.Domain;

/// <summary>
/// Settles a single or accumulator: any lost leg loses the coupon; void legs count at odds 1.00; an all-void coupon
/// refunds the stake. Payouts round down to the minor unit.
/// </summary>
public sealed record CouponSettlement(CouponOutcome Outcome, decimal EffectiveOdds, long Payout)
{
    public static CouponSettlement Of(long stake, IReadOnlyCollection<SettledLeg> legs)
    {
        if (legs.Count == 0)
        {
            throw new ArgumentException("A coupon has at least one leg.", nameof(legs));
        }

        if (legs.Any(l => l.Outcome == LegOutcome.Lost))
        {
            return new CouponSettlement(CouponOutcome.Lost, 0m, 0);
        }

        var odds = legs.Where(l => l.Outcome == LegOutcome.Won).Aggregate(1m, (total, leg) => total * leg.Odds);
        var outcome = legs.All(l => l.Outcome == LegOutcome.Void) ? CouponOutcome.Void : CouponOutcome.Won;
        return new CouponSettlement(outcome, odds, (long)decimal.Floor(stake * odds));
    }

    public bool SameAs(CouponSettlement? other) => other is not null && other.Outcome == Outcome && other.Payout == Payout;
}
