namespace SwiftBets.Settlement.Application.Ports;

/// <summary>
/// Token-guarded progress per coupon (Redis). Each leg evaluation carries a token (leg id and result version); a
/// token counts once, so a redelivered evaluation cannot advance the coupon twice.
/// </summary>
public interface IProgressCounter
{
    /// <returns>Whether the token was new, and how many distinct legs are resolved; -1 once the coupon is final.</returns>
    Task<(bool Applied, int ResolvedLegs)> RecordAsync(Guid couponId, Guid legId, int resultVersion);

    Task<int> ResolvedLegsAsync(Guid couponId);

    /// <summary>Stops the Lua script counting any further evaluation for a cashed-out coupon.</summary>
    Task MarkFinalAsync(Guid couponId);

    Task RebuildAsync(Guid couponId, IReadOnlyList<LegEvaluation> evaluations);
}
