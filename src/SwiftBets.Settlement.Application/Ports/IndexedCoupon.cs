namespace SwiftBets.Settlement.Application.Ports;

public sealed record IndexedCoupon(Guid CouponId, Guid PunterId, long Stake, string Currency, int LegCount, DateTimeOffset? PlacedAt = null);
