using SwiftBets.Contracts.Offer;

namespace SwiftBets.Settlement.Domain.Tests;

public sealed class BuilderRulesTests
{
    private static readonly BetBuilderLegV1 Three = new(
        [new("m-a", "home", 2m), new("m-b", "over", 2m), new("m-c", "yes", 2m)],
        new Dictionary<string, decimal> { ["home.over"] = 1.1m }, 0m);

    [Fact]
    public void A_void_component_reprices_the_rest_when_two_remain() =>
        BuilderRules.Evaluate(Three, c => c.SelectionId == "yes" ? LegOutcome.Void : LegOutcome.Won).ShouldBe(new LegVerdict(LegOutcome.Won, 4.40m));

    [Fact]
    public void A_lost_component_loses_the_leg_even_while_others_are_unknown() =>
        BuilderRules.Evaluate(Three, c => c.SelectionId == "home" ? LegOutcome.Lost : null).ShouldBe(new LegVerdict(LegOutcome.Lost));
}
