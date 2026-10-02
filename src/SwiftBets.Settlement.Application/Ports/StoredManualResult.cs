using SwiftBets.Contracts.Trading;

namespace SwiftBets.Settlement.Application.Ports;

/// <summary>A manual result as applied, kept so coupons indexed after it receive it too.</summary>
public sealed record StoredManualResult(
    Guid ManualResultId, string FixtureId, ManualResultScope Scope, ManualResultAction Action, string? MarketId, Guid? CouponId,
    string? WinningSelectionId, DateTimeOffset? VoidFrom, int Version, DateTimeOffset IssuedAt);
