SELECT e.LegId, e.ResultVersion, e.Outcome, COALESCE(e.Odds, l.Odds) AS Odds, l.IsBanker, l.Position
FROM
(
    SELECT LegId, ResultVersion, Outcome, Odds, ROW_NUMBER() OVER (PARTITION BY LegId ORDER BY ResultVersion DESC) AS Recency
    FROM settlement.LegEvaluations
    WHERE CouponId = @CouponId
) e
JOIN settlement.Legs l ON l.LegId = e.LegId
WHERE e.Recency = 1
ORDER BY l.Position;
