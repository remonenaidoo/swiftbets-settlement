SELECT FixtureId, ResultVersion AS Version, State, HomeGoals, AwayGoals
FROM settlement.Results WITH (UPDLOCK, ROWLOCK)
WHERE FixtureId IN @FixtureIds;
