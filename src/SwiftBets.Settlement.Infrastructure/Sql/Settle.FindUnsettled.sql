SELECT TOP (@Limit) c.CouponId
FROM settlement.Coupons c
WHERE c.SettlementPending = 1
  AND c.LastEvaluatedAt < @Cutoff
  AND (SELECT COUNT(DISTINCT e.LegId) FROM settlement.LegEvaluations e WHERE e.CouponId = c.CouponId) = c.LegCount
ORDER BY c.LastEvaluatedAt;
