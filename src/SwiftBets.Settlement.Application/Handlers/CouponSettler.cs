using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// Stage two, the only writer of settlements. Reads the latest evaluation of every leg from SQL (the source of truth)
/// under a coupon lock; a new settlement version is written only when the outcome or payout actually changes.
/// </summary>
public sealed class CouponSettler(ISettlementStore store, TimeProvider time)
{
    public async Task<CouponSettledV2?> SettleAsync(Guid couponId)
    {
        await using var transaction = await store.BeginAsync();
        var coupon = await transaction.LockCouponAsync(couponId);
        if (coupon is null || await transaction.GetFinalStateAsync(couponId) is not null)
        {
            return null;
        }

        var evaluations = await transaction.GetLatestEvaluationsAsync(couponId);
        if (evaluations.Count < coupon.LegCount)
        {
            return null;
        }

        var bets = await transaction.GetBetsAsync(couponId);
        var legs = evaluations.OrderBy(e => e.Position).Select(e => new SettledLeg(e.Odds, e.Outcome, e.IsBanker)).ToList();
        var settlement = bets.Count == 0 ? CouponSettlement.Of(coupon.Stake, legs) : CouponSettlement.Of(bets, legs);
        var previous = await transaction.GetLatestSettlementAsync(couponId);
        if (previous is not null && previous.Outcome == settlement.Outcome && previous.Payout == settlement.Payout)
        {
            await transaction.ClearPendingAsync(couponId);
            await transaction.CommitAsync();
            return null;
        }

        var version = (previous?.Version ?? 0) + 1;
        await transaction.InsertSettlementAsync(couponId, version, settlement);
        var settled = new CouponSettledV2(couponId, coupon.PunterId, version, SettlementMapping.Map(settlement.Outcome),
            new Money(coupon.Stake, coupon.Currency), new Money(settlement.Payout, coupon.Currency),
            [.. settlement.Bets.Select(b => new BetSettlementV2(b.BetId, SettlementMapping.Map(b.Outcome), b.WinningLines, b.VoidLines, b.LosingLines, new Money(b.Return, coupon.Currency),
                b.BoostBonus > 0 ? new Money(b.BoostBonus, coupon.Currency) : null))],
            time.GetUtcNow());
        await transaction.EnqueueAsync(Topics.CouponSettledV2, couponId.ToString(), settled);
        await transaction.CommitAsync();
        return settled;
    }
}
