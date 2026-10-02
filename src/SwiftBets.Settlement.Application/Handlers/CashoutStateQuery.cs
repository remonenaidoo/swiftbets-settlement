using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>What the cashout service prices from: the coupon's legs at placed odds, which have resolved, and its state.</summary>
public sealed class CashoutStateQuery(ISettlementStore store)
{
    public enum CouponState
    {
        Open,
        Settled,
        CashedOut,
    }

    public sealed record State(Guid CouponId, Guid PunterId, long Stake, string Currency, CouponState Coupon, bool SingleLine, IReadOnlyList<CouponLeg> Legs);

    public async Task<State?> GetAsync(Guid couponId)
    {
        await using var transaction = await store.BeginAsync();
        var coupon = await transaction.LockCouponAsync(couponId);
        if (coupon is null)
        {
            return null;
        }

        var legs = await transaction.GetCouponLegsAsync(couponId);
        var bets = await transaction.GetBetsAsync(couponId);
        var state = await transaction.GetFinalStateAsync(couponId) == FinalState.CashedOut ? CouponState.CashedOut
            : legs.All(l => l.Outcome is not null) ? CouponState.Settled : CouponState.Open;
        return new State(coupon.CouponId, coupon.PunterId, coupon.Stake, coupon.Currency, state, CashoutRules.IsSingleLine(bets, legs.Count(l => !l.IsBanker)), legs);
    }
}
