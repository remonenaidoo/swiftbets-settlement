namespace SwiftBets.Settlement.Domain;

public sealed record SettledLeg(decimal Odds, LegOutcome Outcome);
