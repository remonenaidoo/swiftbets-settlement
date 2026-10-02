SELECT CouponId, PunterId, Stake, Currency, LegCount, PlacedAt FROM settlement.Coupons WITH (UPDLOCK, ROWLOCK) WHERE CouponId = @CouponId;
