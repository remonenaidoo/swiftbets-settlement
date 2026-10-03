SELECT FixtureId, ResultVersion AS Version, State, HomeGoals, AwayGoals, HomeGames, AwayGames, Winners
FROM settlement.Results WITH (UPDLOCK, HOLDLOCK)
WHERE FixtureId = @FixtureId;
