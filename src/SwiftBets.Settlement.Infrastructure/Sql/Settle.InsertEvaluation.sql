INSERT INTO settlement.LegEvaluations (LegId, ResultVersion, CouponId, Outcome, EvaluatedAt)
SELECT @LegId, @ResultVersion, @CouponId, @Outcome, @Now
WHERE NOT EXISTS (SELECT 1 FROM settlement.LegEvaluations WHERE LegId = @LegId AND ResultVersion = @ResultVersion);
IF @@ROWCOUNT = 1
BEGIN
    UPDATE settlement.Coupons SET SettlementPending = 1, LastEvaluatedAt = @Now WHERE CouponId = @CouponId;
    SELECT 1;
END
ELSE
    SELECT 0;
