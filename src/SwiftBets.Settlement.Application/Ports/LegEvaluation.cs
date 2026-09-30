using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Ports;

public sealed record LegEvaluation(Guid LegId, int ResultVersion, LegOutcome Outcome, decimal Odds);
