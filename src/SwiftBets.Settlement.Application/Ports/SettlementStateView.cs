namespace SwiftBets.Settlement.Application.Ports;

public sealed record SettlementStateView(int Version, string Outcome, long Payout, DateTimeOffset SettledAt);
