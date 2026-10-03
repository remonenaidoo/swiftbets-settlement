namespace SwiftBets.Settlement.Domain.Tests;

public sealed class LegRulesTests
{
    [Fact]
    public void Home_selection_wins_when_the_home_side_scores_more() =>
        LegRules.Evaluate("f-1x2", "home", new FixtureResult("f", 1, ResultState.Official, 2, 1)).ShouldBe(LegOutcome.Won);

    [Fact]
    public void Any_selection_on_a_void_result_is_void() =>
        LegRules.Evaluate("f-ou25", "over", new FixtureResult("f", 1, ResultState.Void, 4, 0)).ShouldBe(LegOutcome.Void);

    [Fact]
    public void A_handicap_on_a_whole_line_pushes_to_void_and_a_half_line_total_is_decided()
    {
        var result = new FixtureResult("f", 1, ResultState.Official, 110, 103);

        LegRules.Evaluate("f-hcp", "home:-7.0", result).ShouldBe(LegOutcome.Void);
        LegRules.Evaluate("f-hcp", "away:+7.5", result).ShouldBe(LegOutcome.Won);
        LegRules.Evaluate("f-pts", "under:212.5", result).ShouldBe(LegOutcome.Lost);
        LegRules.Evaluate("f-games", "over:22.5", result with { HomeGames = 13, AwayGames = 10 }).ShouldBe(LegOutcome.Won);
        LegRules.Evaluate("f-winner", "team-bulls", result with { Winners = ["team-bulls"] }).ShouldBe(LegOutcome.Won);
    }

    [Fact]
    public void A_two_way_market_that_ends_level_and_an_outright_with_no_winner_are_void()
    {
        var level = new FixtureResult("f", 1, ResultState.Official, 160, 160);

        LegRules.Evaluate("f-mw", "home", level).ShouldBe(LegOutcome.Void);
        LegRules.Evaluate("f-topbat", "player-jos-buttler", level).ShouldBe(LegOutcome.Void);
    }
}
