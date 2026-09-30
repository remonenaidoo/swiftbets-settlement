SELECT CouponId, PunterId, Stake, Currency, LegCount FROM settlement.Coupons WITH (UPDLOCK, ROWLOCK) WHERE CouponId = @CouponId;
