namespace SwiftBets.Settlement.Domain;

/// <summary>A published result. A later version always wins; within a version, a stronger status wins (void over correction over official over provisional).</summary>
public sealed record FixtureResult(string FixtureId, int Version, ResultState State, int HomeGoals, int AwayGoals)
{
    public bool Outranks(FixtureResult? current) =>
        current is null || Version > current.Version || (Version == current.Version && State > current.State);

    /// <summary>Provisional scores are recorded but never settled on.</summary>
    public bool IsSettleable => State != ResultState.Provisional;
}
