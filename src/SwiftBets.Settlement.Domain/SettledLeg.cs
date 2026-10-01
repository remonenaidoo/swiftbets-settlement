namespace SwiftBets.Settlement.Domain;

/// <summary>A leg's latest outcome. Legs are passed in coupon order; a banker is in every line of every bet.</summary>
public sealed record SettledLeg(decimal Odds, LegOutcome Outcome, bool IsBanker = false);
