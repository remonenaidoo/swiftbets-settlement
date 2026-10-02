UPDATE settlement.Coupons SET FinalState = @FinalState, SettlementPending = 0 WHERE CouponId = @CouponId AND FinalState IS NULL;
