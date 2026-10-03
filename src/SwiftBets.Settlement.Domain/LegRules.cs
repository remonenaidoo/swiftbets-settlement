using SwiftBets.Contracts.Offer;

namespace SwiftBets.Settlement.Domain;

/// <summary>
/// Resolves a leg from its market and selection ids alone (offer contract): the market id's suffix names the type, a line
/// selection carries its line (<c>home:-1.5</c>, <c>over:215.5</c>). A line bet that lands exactly on the line pushes and
/// is void; a two-way winner market that ends level is void; a player or outright market with no named winner is void.
/// </summary>
public static class LegRules
{
    public static LegOutcome Evaluate(string marketId, string selectionId, FixtureResult result)
    {
        if (result.State == ResultState.Void)
        {
            return LegOutcome.Void;
        }

        var (home, away) = (result.HomeGoals, result.AwayGoals);
        return MarketIds.TypeOf(marketId) switch
        {
            MarketType.TopBatter or MarketType.CompetitionWinner => result.Winners is not { Count: > 0 } winners ? LegOutcome.Void : winners.Contains(selectionId) ? LegOutcome.Won : LegOutcome.Lost,
            MarketType.MatchWinner => home == away ? LegOutcome.Void : Won(selectionId switch
            {
                SelectionIds.Home => home > away,
                SelectionIds.Away => away > home,
                _ => throw Unknown(selectionId),
            }),
            MarketType.SetHandicap or MarketType.Handicap => Handicap(selectionId, home - away),
            MarketType.TotalPoints => Total(selectionId, home + away),
            MarketType.TotalGames => result.HomeGames is { } hg && result.AwayGames is { } ag ? Total(selectionId, hg + ag) : LegOutcome.Void,
            _ => Won(selectionId switch
            {
                SelectionIds.Home => home > away,
                SelectionIds.Draw => home == away,
                SelectionIds.Away => away > home,
                SelectionIds.Over => home + away > 2,
                SelectionIds.Under => home + away < 3,
                _ => throw Unknown(selectionId),
            }),
        };
    }

    /// <summary>The side's margin plus its line: above zero wins, zero pushes (void), below loses.</summary>
    private static LegOutcome Handicap(string selectionId, int homeMargin)
    {
        if (!SelectionIds.TryParseLine(selectionId, out var side, out var line) || side is not (SelectionIds.Home or SelectionIds.Away))
        {
            throw Unknown(selectionId);
        }

        return Compare((side == SelectionIds.Home ? homeMargin : -homeMargin) + line);
    }

    private static LegOutcome Total(string selectionId, int total)
    {
        if (!SelectionIds.TryParseLine(selectionId, out var side, out var line) || side is not (SelectionIds.Over or SelectionIds.Under))
        {
            throw Unknown(selectionId);
        }

        return Compare(side == SelectionIds.Over ? total - line : line - total);
    }

    private static LegOutcome Compare(decimal edge) => edge > 0 ? LegOutcome.Won : edge < 0 ? LegOutcome.Lost : LegOutcome.Void;

    private static LegOutcome Won(bool won) => won ? LegOutcome.Won : LegOutcome.Lost;

    private static ArgumentOutOfRangeException Unknown(string selectionId) => new(nameof(selectionId), selectionId, "Unknown selection.");
}
