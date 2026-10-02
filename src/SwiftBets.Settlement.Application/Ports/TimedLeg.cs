namespace SwiftBets.Settlement.Application.Ports;

/// <summary>An indexed leg with its coupon's placement time (null if indexed before times were stored) and final state.</summary>
public sealed record TimedLeg(IndexedLeg Leg, DateTimeOffset? PlacedAt, Domain.FinalState? FinalState = null);
