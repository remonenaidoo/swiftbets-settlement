SELECT l.LegId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds, l.IsBanker, l.Position, latest.Outcome
FROM settlement.Legs l
OUTER APPLY (
    SELECT TOP (1) e.Outcome FROM settlement.LegEvaluations e WHERE e.LegId = l.LegId ORDER BY e.ResultVersion DESC
) latest
WHERE l.CouponId = @CouponId
ORDER BY l.Position;
