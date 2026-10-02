using Microsoft.Extensions.Logging;
using SwiftBets.Contracts.Messaging;
using LegEvaluatedV1 = SwiftBets.Contracts.Settlement.LegEvaluatedV1;
using SwiftBets.Contracts.Trading;
using SwiftBets.Settlement.Application.Ports;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// A trader's result evaluates every leg in scope at a manual version above any feed version, so a later feed result
/// cannot undo it and the settler resettles as usual. A cashed-out coupon is final: it is rejected explicitly, never
/// re-evaluated. Idempotent on <c>ManualResultId</c>.
/// </summary>
public sealed partial class ApplyManualResultHandler(ISettlementStore store, TimeProvider time, ILogger<ApplyManualResultHandler> logger)
{
    public const string Consumer = "settlement.manual-results";

    private static readonly DateTimeOffset VersionEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Feed versions stay far below this; later manual results get higher versions.</summary>
    public static int ManualVersion(DateTimeOffset issuedAt) => 1_000_000 + (int)Math.Max(0, (issuedAt - VersionEpoch).TotalSeconds);

    public async Task<int> HandleAsync(ManualResultV1 manual)
    {
        ArgumentNullException.ThrowIfNull(manual);
        if (manual.Action == ManualResultAction.TimeVoid && manual.VoidFrom is null)
        {
            LogTimeVoidWithoutCutOff(logger, manual.ManualResultId);
            return 0;
        }

        var version = ManualVersion(manual.IssuedAt);
        var evaluated = 0;
        await using var transaction = await store.BeginAsync();
        if (!await transaction.TryRecordInboxAsync(Consumer, manual.ManualResultId))
        {
            return 0;
        }

        var stored = ManualResultRules.From(manual, version);
        await transaction.SaveManualResultAsync(stored);
        var touched = await LegsTouchedAsync(transaction, manual, stored);
        foreach (var couponId in touched.Where(t => t.FinalState is not null).Select(t => t.Leg.CouponId).Distinct())
        {
            await transaction.EnqueueAsync(Topics.ManualResultRejected, couponId.ToString(), new ManualResultRejectedV1(manual.ManualResultId, couponId,
                ManualResultRejectedV1.CouponCashedOut, "The coupon was cashed out; its settlement is final.", time.GetUtcNow()));
        }

        foreach (var leg in touched.Where(t => t.FinalState is null).Select(t => t.Leg))
        {
            var outcome = ManualResultRules.Outcome(stored, leg);
            if (await transaction.TryInsertEvaluationAsync(leg, version, outcome))
            {
                await transaction.EnqueueAsync(Topics.LegEvaluated, leg.CouponId.ToString(),
                    new LegEvaluatedV1(leg.CouponId, leg.LegId, leg.FixtureId, version, SettlementMapping.Map(outcome), time.GetUtcNow()));
                evaluated++;
            }
        }

        await transaction.CommitAsync();
        return evaluated;
    }

    /// <summary>
    /// The legs this result would change. A time-void touches only coupons placed at or after the cut-off; a coupon with
    /// no stored placement time (indexed before settlement 0004) is never voided on a guess, only counted and logged.
    /// Cashed-out coupons are included so the caller can reject them explicitly instead of skipping them silently.
    /// </summary>
    private async Task<IReadOnlyList<TimedLeg>> LegsTouchedAsync(ISettlementTransaction transaction, ManualResultV1 manual, StoredManualResult stored)
    {
        var inScope = (await transaction.GetTimedLegsForFixtureAsync(manual.FixtureId)).Where(t => ManualResultRules.InScope(stored, t.Leg)).ToList();
        if (manual.Action != ManualResultAction.TimeVoid)
        {
            return inScope;
        }

        var unknown = inScope.Count(t => t.PlacedAt is null);
        if (unknown > 0)
        {
            LogPlacementTimeUnknown(logger, manual.ManualResultId, unknown);
        }

        return [.. inScope.Where(t => ManualResultRules.AppliesTo(stored, t.PlacedAt))];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manual result {ManualResultId} is a time-void without a VoidFrom cut-off; skipped.")]
    private static partial void LogTimeVoidWithoutCutOff(ILogger logger, Guid manualResultId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Time-void {ManualResultId} left {Legs} legs alone: their coupons were indexed before placement times were stored.")]
    private static partial void LogPlacementTimeUnknown(ILogger logger, Guid manualResultId, int legs);
}
