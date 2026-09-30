namespace SwiftBets.Settlement.Domain.Tests;

public sealed class FixtureResultTests
{
    [Fact]
    public void Correction_at_a_later_version_outranks_the_official_result() =>
        new FixtureResult("f", 2, ResultState.Correction, 1, 1).Outranks(new FixtureResult("f", 1, ResultState.Official, 2, 1)).ShouldBeTrue();

    [Fact]
    public void Identical_result_does_not_outrank_itself() =>
        new FixtureResult("f", 1, ResultState.Official, 2, 1).Outranks(new FixtureResult("f", 1, ResultState.Official, 2, 1)).ShouldBeFalse();
}
