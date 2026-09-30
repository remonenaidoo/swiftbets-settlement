UPDATE settlement.Results
SET ResultVersion = @Version, State = @State, HomeGoals = @HomeGoals, AwayGoals = @AwayGoals, PublishedAt = @Now
WHERE FixtureId = @FixtureId;
IF @@ROWCOUNT = 0
    INSERT INTO settlement.Results (FixtureId, ResultVersion, State, HomeGoals, AwayGoals, PublishedAt)
    VALUES (@FixtureId, @Version, @State, @HomeGoals, @AwayGoals, @Now);
