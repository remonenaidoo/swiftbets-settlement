UPDATE settlement.Results
SET ResultVersion = @Version, State = @State, HomeGoals = @HomeGoals, AwayGoals = @AwayGoals, HomeGames = @HomeGames, AwayGames = @AwayGames, Winners = @Winners, PublishedAt = @Now
WHERE FixtureId = @FixtureId;
IF @@ROWCOUNT = 0
    INSERT INTO settlement.Results (FixtureId, ResultVersion, State, HomeGoals, AwayGoals, HomeGames, AwayGames, Winners, PublishedAt)
    VALUES (@FixtureId, @Version, @State, @HomeGoals, @AwayGoals, @HomeGames, @AwayGames, @Winners, @Now);
