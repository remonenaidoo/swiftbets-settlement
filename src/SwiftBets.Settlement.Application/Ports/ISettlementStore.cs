namespace SwiftBets.Settlement.Application.Ports;

public interface ISettlementStore
{
    Task<ISettlementTransaction> BeginAsync();

    /// <summary>Coupons whose every leg is evaluated but whose latest evaluation is newer than their latest settlement, quiet for at least <paramref name="quietFor"/>.</summary>
    Task<IReadOnlyList<Guid>> FindUnsettledCouponsAsync(TimeSpan quietFor, int limit, CancellationToken cancellationToken);

    /// <summary>Legs whose fixture has a settleable result but which were never evaluated.</summary>
    Task<IReadOnlyList<(IndexedLeg Leg, Domain.FixtureResult Result)>> FindUnevaluatedLegsAsync(int limit, CancellationToken cancellationToken);
}
