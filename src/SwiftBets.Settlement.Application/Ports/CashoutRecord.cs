namespace SwiftBets.Settlement.Application.Ports;

public sealed record CashoutRecord(Guid CashoutId, Guid CouponId, long Amount, string Currency, int SettlementVersion, DateTimeOffset CashedOutAt);
