SELECT LegId, CouponId, FixtureId, MarketId, SelectionId, Odds, IsBanker, Position, Builder FROM settlement.Legs WHERE FixtureId = @FixtureId;
