SELECT TOP (1) Version, Outcome, Payout FROM settlement.Settlements WHERE CouponId = @CouponId ORDER BY Version DESC;
