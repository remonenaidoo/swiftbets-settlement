UPDATE settlement.Coupons SET SettlementPending = 0 WHERE CouponId = @CouponId AND SettlementPending = 1;
