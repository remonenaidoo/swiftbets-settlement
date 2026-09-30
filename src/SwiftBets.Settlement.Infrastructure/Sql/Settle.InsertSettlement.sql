INSERT INTO settlement.Settlements (CouponId, Version, Outcome, EffectiveOdds, Payout, SettledAt)
VALUES (@CouponId, @Version, @Outcome, @EffectiveOdds, @Payout, @Now);
UPDATE settlement.Coupons SET SettlementPending = 0 WHERE CouponId = @CouponId;
