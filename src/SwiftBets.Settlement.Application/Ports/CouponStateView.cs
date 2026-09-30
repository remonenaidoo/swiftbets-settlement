namespace SwiftBets.Settlement.Application.Ports;

/// <summary>Everything settlement knows about one coupon, for operators and Steward's diagnosis tools.</summary>
public sealed record CouponStateView(
    Guid CouponId,
    int LegCount,
    bool SettlementPending,
    DateTimeOffset? LastEvaluatedAt,
    IReadOnlyList<LegStateView> Legs,
    IReadOnlyList<SettlementStateView> Settlements,
    int RedisResolvedLegs);
