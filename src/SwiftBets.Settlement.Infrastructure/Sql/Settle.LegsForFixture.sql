SELECT LegId, CouponId, FixtureId, MarketId, SelectionId, Odds FROM settlement.Legs WHERE FixtureId = @FixtureId;
