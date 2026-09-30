SELECT FixtureId, ResultVersion AS Version, State, HomeGoals, AwayGoals
FROM settlement.Results WITH (UPDLOCK, HOLDLOCK)
WHERE FixtureId = @FixtureId;
