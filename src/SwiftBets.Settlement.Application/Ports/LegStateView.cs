namespace SwiftBets.Settlement.Application.Ports;

public sealed record LegStateView(Guid LegId, string FixtureId, string SelectionId, decimal Odds, int? LatestResultVersion, string? Outcome);
