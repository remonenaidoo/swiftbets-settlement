INSERT INTO settlement.ManualResults (ManualResultId, FixtureId, Scope, Action, MarketId, CouponId, WinningSelectionId, VoidFrom, Version, IssuedAt)
SELECT @ManualResultId, @FixtureId, @Scope, @Action, @MarketId, @CouponId, @WinningSelectionId, @VoidFrom, @Version, @IssuedAt
WHERE NOT EXISTS (SELECT 1 FROM settlement.ManualResults WHERE ManualResultId = @ManualResultId);
