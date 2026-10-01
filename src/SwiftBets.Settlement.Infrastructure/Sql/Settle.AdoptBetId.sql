UPDATE settlement.Bets
SET BetId = @BetId
WHERE CouponId = @CouponId AND Folds = @Folds AND UnitStake = @UnitStake AND BetId <> @BetId
  AND (SELECT COUNT(*) FROM settlement.Bets WHERE CouponId = @CouponId) = 1;
