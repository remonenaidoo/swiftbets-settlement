INSERT INTO settlement.Coupons (CouponId, PunterId, Stake, Currency, LegCount, IndexedAt, PlacedAt)
SELECT @CouponId, @PunterId, @Stake, @Currency, @LegCount, @Now, @PlacedAt
WHERE NOT EXISTS (SELECT 1 FROM settlement.Coupons WITH (UPDLOCK, HOLDLOCK) WHERE CouponId = @CouponId);
