namespace SwiftBets.Settlement.Application.Ports;

/// <summary>A coupon's leg with its latest evaluation, if any; null means the leg is still open.</summary>
public sealed record CouponLeg(Guid LegId, string FixtureId, string MarketId, string SelectionId, decimal Odds, bool IsBanker, int Position, Domain.LegOutcome? Outcome);
