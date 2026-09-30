namespace SwiftBets.Settlement.Application.Ports;

/// <summary>
/// Token-guarded progress per coupon (Redis). Each leg evaluation carries a token (leg id and result version); a
/// token counts once, so a redelivered evaluation cannot advance the coupon twice.
/// </summary>
public interface IProgressCounter
{
    /// <returns>Whether the token was new, and how many distinct legs are resolved.</returns>
    Task<(bool Applied, int ResolvedLegs)> RecordAsync(Guid couponId, Guid legId, int resultVersion);

    Task<int> ResolvedLegsAsync(Guid couponId);

    Task RebuildAsync(Guid couponId, IReadOnlyList<LegEvaluation> evaluations);
}
