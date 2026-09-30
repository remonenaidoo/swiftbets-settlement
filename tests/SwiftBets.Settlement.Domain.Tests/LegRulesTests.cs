namespace SwiftBets.Settlement.Domain.Tests;

public sealed class LegRulesTests
{
    [Fact]
    public void Home_selection_wins_when_the_home_side_scores_more() =>
        LegRules.Evaluate("home", new FixtureResult("f", 1, ResultState.Official, 2, 1)).ShouldBe(LegOutcome.Won);

    [Fact]
    public void Any_selection_on_a_void_result_is_void() =>
        LegRules.Evaluate("over", new FixtureResult("f", 1, ResultState.Void, 4, 0)).ShouldBe(LegOutcome.Void);
}
