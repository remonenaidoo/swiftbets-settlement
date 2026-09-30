SELECT CouponId, LegCount, SettlementPending, LastEvaluatedAt FROM settlement.Coupons WHERE CouponId = @CouponId;
SELECT l.LegId, l.FixtureId, l.SelectionId, l.Odds, e.ResultVersion AS LatestResultVersion, e.Outcome
FROM settlement.Legs l
OUTER APPLY (SELECT TOP (1) ResultVersion, Outcome FROM settlement.LegEvaluations x WHERE x.LegId = l.LegId ORDER BY ResultVersion DESC) e
WHERE l.CouponId = @CouponId;
SELECT Version, Outcome, Payout, SettledAt FROM settlement.Settlements WHERE CouponId = @CouponId ORDER BY Version;
