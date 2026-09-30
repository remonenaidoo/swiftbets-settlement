namespace SwiftBets.Settlement.Domain.Tests;

public sealed class CouponSettlementTests
{
    [Fact]
    public void Accumulator_with_a_void_leg_pays_on_the_remaining_legs()
    {
        var settlement = CouponSettlement.Of(1_000, [new(2.00m, LegOutcome.Won), new(3.50m, LegOutcome.Void), new(1.50m, LegOutcome.Won)]);

        settlement.Outcome.ShouldBe(CouponOutcome.Won);
        settlement.EffectiveOdds.ShouldBe(3.00m);
        settlement.Payout.ShouldBe(3_000);
    }

    [Fact]
    public void One_lost_leg_loses_the_accumulator() =>
        CouponSettlement.Of(1_000, [new(2.00m, LegOutcome.Won), new(1.50m, LegOutcome.Lost)]).Payout.ShouldBe(0);
}
