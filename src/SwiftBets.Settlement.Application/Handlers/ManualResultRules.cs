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
        ManualResultScope.Market => leg.FixtureId == manual.FixtureId && leg.MarketId == manual.MarketId,
        ManualResultScope.Coupon => leg.CouponId == manual.CouponId,
        _ => false,
    };

    /// <summary>A time-void touches only coupons placed at or after the cut-off; an unknown placement time is never guessed.</summary>
    public static bool AppliesTo(StoredManualResult manual, DateTimeOffset? placedAt) =>
        manual.Action != ManualResultAction.TimeVoid || (manual.VoidFrom is { } from && placedAt >= from);

    public static LegOutcome Outcome(StoredManualResult manual, IndexedLeg leg) =>
        manual.Action is ManualResultAction.Void or ManualResultAction.TimeVoid ? LegOutcome.Void
        : leg.SelectionId == manual.WinningSelectionId ? LegOutcome.Won : LegOutcome.Lost;

    public static StoredManualResult From(ManualResultV1 manual, int version) =>
        new(manual.ManualResultId, manual.FixtureId, manual.Scope, manual.Action, manual.MarketId, manual.CouponId, manual.WinningSelectionId, manual.VoidFrom, version, manual.IssuedAt);
}
