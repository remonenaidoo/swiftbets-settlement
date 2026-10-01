namespace SwiftBets.Settlement.Application.Ports;

/// <summary>Position is the leg's place on the coupon; bet lines index the non-banker legs in that order.</summary>
public sealed record IndexedLeg(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds, bool IsBanker = false, int Position = 0);
