namespace SwiftBets.Settlement.Domain;

/// <summary>
/// A published result. A later version always wins; within a version, a stronger status wins (void over correction over
/// official over provisional). The score is goals, points, runs or (tennis) sets; games and winners carry the rest.
/// </summary>
public sealed record FixtureResult(string FixtureId, int Version, ResultState State, int HomeGoals, int AwayGoals, int? HomeGames = null, int? AwayGames = null, IReadOnlyList<string>? Winners = null)
{
    public bool Outranks(FixtureResult? current) =>
        current is null || Version > current.Version || (Version == current.Version && State > current.State);

    /// <summary>Provisional scores are recorded but never settled on.</summary>
    public bool IsSettleable => State != ResultState.Provisional;

    /// <summary>Winners as stored: selection ids joined by commas (selection ids never contain one).</summary>
    public string? WinnersText => Winners is { Count: > 0 } ? string.Join(',', Winners) : null;

    public static IReadOnlyList<string>? ParseWinners(string? text) => string.IsNullOrEmpty(text) ? null : text.Split(',');
}
