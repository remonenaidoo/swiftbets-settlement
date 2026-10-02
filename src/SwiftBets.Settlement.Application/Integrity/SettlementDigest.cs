using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Integrity;

/// <summary>A coupon's latest settlement, as bet-history's integrity check compares it.</summary>
public sealed record SettlementDigest(Guid CouponId, int Version, CouponOutcome Outcome, long Payout, DateTimeOffset SettledAt);
