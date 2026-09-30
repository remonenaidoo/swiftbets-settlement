namespace SwiftBets.Settlement.Domain;

/// <summary>Selection ids follow the offer contract: home, draw, away (match result) and over, under (2.5 goals).</summary>
public static class LegRules
{
    public static LegOutcome Evaluate(string selectionId, FixtureResult result)
    {
        if (result.State == ResultState.Void)
        {
            return LegOutcome.Void;
        }

        var (home, away) = (result.HomeGoals, result.AwayGoals);
        var won = selectionId switch
        {
            "home" => home > away,
            "draw" => home == away,
            "away" => away > home,
            "over" => home + away > 2,
            "under" => home + away < 3,
            _ => throw new ArgumentOutOfRangeException(nameof(selectionId), selectionId, "Unknown selection."),
        };
        return won ? LegOutcome.Won : LegOutcome.Lost;
    }
}
