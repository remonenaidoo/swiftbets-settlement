using SwiftBets.Contracts.Trading;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>Which legs a manual result touches and what it makes of them; shared by applying it now and by indexing later.</summary>
public static class ManualResultRules
{
    public static bool InScope(StoredManualResult manual, IndexedLeg leg) => manual.Scope switch
    {
        ManualResultScope.Fixture => leg.FixtureId == manual.FixtureId,
        ManualResultScope.Market => leg.FixtureId == manual.FixtureId && (leg.MarketId == manual.MarketId || (leg.BuilderLeg?.Components.Any(c => c.MarketId == manual.MarketId) ?? false)),
        ManualResultScope.Coupon => leg.CouponId == manual.CouponId,
        _ => false,
    };

    /// <summary>A time-void touches only coupons placed at or after the cut-off; an unknown placement time is never guessed.</summary>
    public static bool AppliesTo(StoredManualResult manual, DateTimeOffset? placedAt) =>
        manual.Action != ManualResultAction.TimeVoid || (manual.VoidFrom is { } from && placedAt >= from);

    public static LegOutcome Outcome(StoredManualResult manual, IndexedLeg leg) =>
        manual.Action is ManualResultAction.Void or ManualResultAction.TimeVoid ? LegOutcome.Void
        : leg.SelectionId == manual.WinningSelectionId ? LegOutcome.Won : LegOutcome.Lost;

    /// <summary>
    /// A bet builder takes a fixture or market result per component: components the result covers get its outcome, the
    /// others their feed result (unknown until one lands). A coupon-scoped result judges the leg as a whole.
    /// </summary>
    public static LegVerdict Verdict(StoredManualResult manual, IndexedLeg leg, FixtureResult? feed)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(leg);
        if (leg.BuilderLeg is not { } builder || manual.Scope == ManualResultScope.Coupon)
        {
            return new LegVerdict(Outcome(manual, leg));
        }

        var voids = manual.Action is ManualResultAction.Void or ManualResultAction.TimeVoid;
        return BuilderRules.Evaluate(builder, c =>
            c.MarketId == manual.MarketId || (manual.MarketId is null && voids) ? Outcome(manual, leg with { MarketId = c.MarketId, SelectionId = c.SelectionId })
            : manual.MarketId is null && c.SelectionId == manual.WinningSelectionId ? LegOutcome.Won
            : feed is { IsSettleable: true } ? LegRules.Evaluate(c.SelectionId, feed) : null);
    }

    public static StoredManualResult From(ManualResultV1 manual, int version) =>
        new(manual.ManualResultId, manual.FixtureId, manual.Scope, manual.Action, manual.MarketId, manual.CouponId, manual.WinningSelectionId, manual.VoidFrom, version, manual.IssuedAt);
}
