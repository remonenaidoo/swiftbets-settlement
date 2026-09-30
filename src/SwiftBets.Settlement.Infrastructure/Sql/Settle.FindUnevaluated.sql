SELECT TOP (@Limit)
       l.LegId, l.CouponId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds,
       r.FixtureId AS ResultFixtureId, r.ResultVersion, r.State, r.HomeGoals, r.AwayGoals
FROM settlement.Results r
JOIN settlement.Legs l ON l.FixtureId = r.FixtureId
WHERE r.PublishedAt > @Since
  AND r.State <> 1
  AND NOT EXISTS (SELECT 1 FROM settlement.LegEvaluations e WHERE e.LegId = l.LegId AND e.ResultVersion = r.ResultVersion);
