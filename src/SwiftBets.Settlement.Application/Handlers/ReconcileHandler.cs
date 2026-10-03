using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// Finds divergence between Redis progress and SQL truth: coupons fully evaluated in SQL but never (re)settled, and
/// legs whose result landed without being evaluated. It repairs them from SQL, and reports each as a stuck coupon.
/// </summary>
public sealed class ReconcileHandler(ISettlementStore store, IProgressCounter counter, CouponSettler settler, TimeProvider time)
{
    public async Task<int> ReconcileAsync(TimeSpan quietFor, CancellationToken cancellationToken)
    {
        var repaired = 0;
        foreach (var (leg, result) in await store.FindUnevaluatedLegsAsync(200, cancellationToken))
        {
            await using var transaction = await store.BeginAsync();
            var verdict = BuilderRules.Evaluate(leg.SelectionId, leg.BuilderLeg, result);
            if (verdict.Outcome is { } outcome && await transaction.TryInsertEvaluationAsync(leg, result.Version, outcome, verdict.Odds))
            {
                await transaction.EnqueueAsync(Topics.LegEvaluated, leg.CouponId.ToString(),
                    new LegEvaluatedV1(leg.CouponId, leg.LegId, leg.FixtureId, result.Version, SettlementMapping.Map(outcome), time.GetUtcNow()));
            }

            await transaction.CommitAsync();
        }

        foreach (var couponId in await store.FindUnsettledCouponsAsync(quietFor, 200, cancellationToken))
        {
            repaired += await RepairAsync(couponId, "evaluated_but_unsettled") ? 1 : 0;
        }

        return repaired;
    }

    /// <summary>Rebuilds the coupon's Redis progress from SQL and settles it; also backs the operator refresh command.</summary>
    public async Task<bool> RepairAsync(Guid couponId, string reason)
    {
        IReadOnlyList<LegEvaluation> evaluations;
        int legCount;
        await using (var read = await store.BeginAsync())
        {
            var coupon = await read.LockCouponAsync(couponId);
            if (coupon is null)
            {
                return false;
            }

            legCount = coupon.LegCount;
            evaluations = await read.GetLatestEvaluationsAsync(couponId);
        }

        var redisBefore = await counter.ResolvedLegsAsync(couponId);
        await counter.RebuildAsync(couponId, evaluations);
        var settled = await settler.SettleAsync(couponId);

        await using var report = await store.BeginAsync();
        await report.EnqueueAsync(Topics.StuckCoupon, couponId.ToString(),
            new StuckCouponV1(couponId, $"{reason}; redis had {redisBefore} of {legCount} legs", evaluations.Count, legCount, settled is not null, time.GetUtcNow()));
        await report.CommitAsync();
        return settled is not null;
    }
}
