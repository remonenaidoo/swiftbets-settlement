SELECT l.LegId, l.CouponId, l.FixtureId, l.MarketId, l.SelectionId, l.Odds, l.IsBanker, l.Position, c.PlacedAt, c.FinalState
FROM settlement.Legs l
JOIN settlement.Coupons c ON c.CouponId = l.CouponId
WHERE l.FixtureId = @FixtureId;
