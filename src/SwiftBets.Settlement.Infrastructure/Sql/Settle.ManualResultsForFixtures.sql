SELECT ManualResultId, FixtureId, Scope, Action, MarketId, CouponId, WinningSelectionId, VoidFrom, Version, IssuedAt
FROM settlement.ManualResults
WHERE FixtureId IN @FixtureIds
ORDER BY Version;
