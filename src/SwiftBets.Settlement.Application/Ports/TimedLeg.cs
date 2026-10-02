namespace SwiftBets.Settlement.Application.Ports;

/// <summary>An indexed leg with its coupon's placement time; null when the coupon was indexed before placement times were stored.</summary>
public sealed record TimedLeg(IndexedLeg Leg, DateTimeOffset? PlacedAt);
