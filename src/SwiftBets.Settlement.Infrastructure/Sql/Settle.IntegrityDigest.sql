SELECT s.CouponId, s.Version, s.Outcome, s.Payout, s.SettledAt
FROM settlement.Settlements s
WHERE s.CouponId IN @Ids
  AND s.Version = (SELECT MAX(l.Version) FROM settlement.Settlements l WHERE l.CouponId = s.CouponId);
