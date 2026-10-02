SELECT CashoutId, CouponId, Amount, Currency, SettlementVersion, CashedOutAt FROM settlement.Cashouts WHERE CashoutId = @CashoutId;
