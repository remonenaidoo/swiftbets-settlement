namespace SwiftBets.Settlement.Domain.Tests;

public sealed class SystemSettlementTests
{
    private static readonly SettlementBet Trixie = new(Guid.NewGuid(), [2, 3], 100);

    private static SettledLeg Won(decimal odds, bool banker = false) => new(odds, LegOutcome.Won, banker);

    private static SettledLeg Lost(bool banker = false) => new(2m, LegOutcome.Lost, banker);

    private static SettledLeg Void(bool banker = false) => new(2m, LegOutcome.Void, banker);

    [Fact]
    public void A_trixie_pays_each_winning_line()
    {
        var settlement = CouponSettlement.Of([Trixie], [Won(2m), Won(3m), Lost()]);

        settlement.Payout.ShouldBe(600);
        settlement.Outcome.ShouldBe(CouponOutcome.Won);
        settlement.EffectiveOdds.ShouldBe(1.5m);
        settlement.Bets.Single().ShouldBe(new SettledBet(Trixie.BetId, CouponOutcome.Won, 1, 0, 3, 600));
    }

    [Fact]
    public void A_lost_banker_loses_every_line()
    {
        var settlement = CouponSettlement.Of([Trixie], [Lost(banker: true), Won(2m), Won(3m), Won(4m)]);

        settlement.ShouldSatisfyAllConditions(s => s.Outcome.ShouldBe(CouponOutcome.Lost), s => s.Payout.ShouldBe(0));
        settlement.Bets.Single().LosingLines.ShouldBe(4);
    }

    [Fact]
    public void Void_legs_count_at_evens_and_an_all_void_coupon_refunds_every_line()
    {
        var oneVoid = CouponSettlement.Of([Trixie], [Won(2m), Won(3m), Void()]);
        var allVoid = CouponSettlement.Of([Trixie], [Void(), Void(), Void()]);

        oneVoid.Payout.ShouldBe(600 + 200 + 300 + 600);
        allVoid.ShouldSatisfyAllConditions(s => s.Outcome.ShouldBe(CouponOutcome.Void), s => s.Payout.ShouldBe(400));
        allVoid.Bets.Single().VoidLines.ShouldBe(4);
    }

    [Fact]
    public void Several_bets_add_up_and_a_bet_returning_nothing_is_lost()
    {
        var singles = new SettlementBet(Guid.NewGuid(), [1], 50);
        var double_ = new SettlementBet(Guid.NewGuid(), [2], 100);

        var settlement = CouponSettlement.Of([singles, double_], [Won(1.5m), Lost()]);

        settlement.Payout.ShouldBe(75);
        settlement.Bets.Select(b => b.Outcome).ShouldBe([CouponOutcome.Won, CouponOutcome.Lost]);
        settlement.EffectiveOdds.ShouldBe(0.375m);
    }

    [Fact]
    public void Each_line_rounds_down_on_its_own()
    {
        var singles = new SettlementBet(Guid.NewGuid(), [1], 1);

        CouponSettlement.Of([singles], [Won(1.333m), Won(1.333m)]).Payout.ShouldBe(2);
    }

    [Fact]
    public void Nothing_to_settle_is_a_programming_error()
    {
        Should.Throw<ArgumentException>(() => CouponSettlement.Of([Trixie], []));
        Should.Throw<ArgumentException>(() => CouponSettlement.Of([], [Won(2m)]));
    }
}
