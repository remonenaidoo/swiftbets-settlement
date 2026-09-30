using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// Consumes leg evaluations. The Redis counter gates the SQL settle: a coupon is settled once all its legs are resolved.
/// A redelivered token is not counted again, but if the coupon is complete the settler still runs (it is a no-op when
/// nothing changed), which covers a crash between the counter and the settlement commit.
/// </summary>
public sealed class SettleCouponHandler(ISettlementStore store, IProgressCounter counter, CouponSettler settler)
{
    public async Task<CouponSettledV1?> HandleAsync(LegEvaluatedV1 evaluated)
    {
        var (_, resolved) = await counter.RecordAsync(evaluated.CouponId, evaluated.LegId, evaluated.ResultVersion);
        await using (var transaction = await store.BeginAsync())
        {
            var coupon = await transaction.LockCouponAsync(evaluated.CouponId);
            if (coupon is null || resolved < coupon.LegCount)
            {
                return null;
            }
        }

        return await settler.SettleAsync(evaluated.CouponId);
    }
}
