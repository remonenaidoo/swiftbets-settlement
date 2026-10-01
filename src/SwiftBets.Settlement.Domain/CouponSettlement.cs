using SwiftBets.Contracts.Placement;

namespace SwiftBets.Settlement.Domain;

/// <summary>A bet as placed: fold sizes over the coupon's non-banker legs, and the stake on each line.</summary>
public sealed record SettlementBet(Guid BetId, IReadOnlyList<int> Folds, long UnitStake);

public sealed record SettledBet(Guid BetId, CouponOutcome Outcome, int WinningLines, int VoidLines, int LosingLines, long Return);

/// <summary>
/// Settles every line of every bet the way placement priced it (<see cref="SystemBets"/>): a lost leg loses the line,
/// void legs count at odds 1.00 so an all-void line returns its stake, and each line rounds down to the minor unit.
/// A bet or coupon is Void when all its lines are void, Lost when it returns nothing, and Won otherwise.
/// </summary>
public sealed record CouponSettlement(CouponOutcome Outcome, decimal EffectiveOdds, long Payout, IReadOnlyList<SettledBet> Bets)
{
    /// <summary>A single or accumulator: one line of every leg, the whole stake on it.</summary>
    public static CouponSettlement Of(long stake, IReadOnlyList<SettledLeg> legs) =>
        Of([new SettlementBet(Guid.Empty, [legs.Count], stake)], legs);

    public static CouponSettlement Of(IReadOnlyList<SettlementBet> bets, IReadOnlyList<SettledLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(bets);
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count == 0 || bets.Count == 0)
        {
            throw new ArgumentException("A coupon has at least one leg and one bet.");
        }

        var bankers = legs.Where(l => l.IsBanker).ToList();
        var others = legs.Where(l => !l.IsBanker).ToList();
        var settled = bets.Select(bet => SettleBet(bet, bankers, others)).ToList();
        var payout = settled.Sum(b => b.Return);
        var stake = bets.Sum(b => b.UnitStake * SystemBets.Lines(others.Count, b.Folds));
        var outcome = settled.All(b => b.Outcome == CouponOutcome.Void) ? CouponOutcome.Void : payout == 0 ? CouponOutcome.Lost : CouponOutcome.Won;
        var odds = stake == 0 ? 0m : decimal.Round((decimal)payout / stake, 6, MidpointRounding.ToZero);
        return new CouponSettlement(outcome, odds, payout, settled);
    }

    public bool SameAs(CouponSettlement? other) => other is not null && other.Outcome == Outcome && other.Payout == Payout;

    private static SettledBet SettleBet(SettlementBet bet, List<SettledLeg> bankers, List<SettledLeg> others)
    {
        int won = 0, voided = 0, lost = 0;
        long returned = 0;
        foreach (var line in SystemBets.Combinations(others.Count, bet.Folds))
        {
            var legs = bankers.Concat(line.Select(i => others[i])).ToList();
            if (legs.Any(l => l.Outcome == LegOutcome.Lost))
            {
                lost++;
                continue;
            }

            if (legs.All(l => l.Outcome == LegOutcome.Void))
            {
                voided++;
            }
            else
            {
                won++;
            }

            var odds = legs.Where(l => l.Outcome == LegOutcome.Won).Aggregate(1m, (total, leg) => total * leg.Odds);
            returned += (long)decimal.Floor(bet.UnitStake * odds);
        }

        var outcome = lost == 0 && won == 0 ? CouponOutcome.Void : returned == 0 ? CouponOutcome.Lost : CouponOutcome.Won;
        return new SettledBet(bet.BetId, outcome, won, voided, lost, returned);
    }
}
