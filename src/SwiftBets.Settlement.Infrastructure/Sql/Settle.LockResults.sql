SELECT FixtureId, ResultVersion AS Version, State, HomeGoals, AwayGoals, HomeGames, AwayGames, Winners
FROM settlement.Results WITH (UPDLOCK, ROWLOCK)
WHERE FixtureId IN @FixtureIds;
