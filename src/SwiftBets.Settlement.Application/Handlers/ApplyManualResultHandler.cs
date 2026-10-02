using Microsoft.Extensions.Logging;
using SwiftBets.Contracts.Messaging;
using LegEvaluatedV1 = SwiftBets.Contracts.Settlement.LegEvaluatedV1;
using SwiftBets.Contracts.Trading;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// A trader's result evaluates every leg in scope at a manual version above any feed version, so a later feed result
/// cannot undo it and the settler resettles as usual. Idempotent on <c>ManualResultId</c>.
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
        if (manual.Action == ManualResultAction.TimeVoid)
        {
            LogTimeVoidUnsupported(logger, manual.ManualResultId);
            return 0;
        }

        var version = ManualVersion(manual.IssuedAt);
        var evaluated = 0;
        await using var transaction = await store.BeginAsync();
        if (!await transaction.TryRecordInboxAsync(Consumer, manual.ManualResultId))
        {
            return 0;
        }

        foreach (var leg in (await transaction.GetLegsForFixtureAsync(manual.FixtureId)).Where(l => InScope(manual, l)))
        {
            var outcome = manual.Action == ManualResultAction.Void ? LegOutcome.Void
                : leg.SelectionId == manual.WinningSelectionId ? LegOutcome.Won : LegOutcome.Lost;
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

    private static bool InScope(ManualResultV1 manual, IndexedLeg leg) => manual.Scope switch
    {
        ManualResultScope.Fixture => true,
        ManualResultScope.Market => leg.MarketId == manual.MarketId,
        ManualResultScope.Coupon => leg.CouponId == manual.CouponId,
        _ => false,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manual result {ManualResultId} is a time-void, which needs placement times (settlement 0004); skipped.")]
    private static partial void LogTimeVoidUnsupported(ILogger logger, Guid manualResultId);
}
