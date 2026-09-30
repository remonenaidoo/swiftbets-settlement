namespace SwiftBets.Settlement.Application.Ports;

public sealed record IndexedLeg(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds);
