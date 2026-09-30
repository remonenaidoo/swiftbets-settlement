SELECT e.LegId, e.ResultVersion, e.Outcome, l.Odds
FROM
(
    SELECT LegId, ResultVersion, Outcome, ROW_NUMBER() OVER (PARTITION BY LegId ORDER BY ResultVersion DESC) AS Recency
    FROM settlement.LegEvaluations
    WHERE CouponId = @CouponId
) e
JOIN settlement.Legs l ON l.LegId = e.LegId
WHERE e.Recency = 1;
