SELECT LegId, CouponId, FixtureId, MarketId, SelectionId, Odds, IsBanker, Position FROM settlement.Legs WHERE FixtureId = @FixtureId;
