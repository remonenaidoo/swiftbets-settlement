SELECT BetId, Folds, UnitStake, AccaBoostPercent FROM settlement.Bets WHERE CouponId = @CouponId ORDER BY BetId;
