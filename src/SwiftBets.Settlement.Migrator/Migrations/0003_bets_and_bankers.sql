-- V2 coupons: legs keep their place on the coupon and may be bankers; a coupon holds one or more bets.
ALTER TABLE settlement.Legs ADD IsBanker bit NOT NULL CONSTRAINT DF_SettlementLegs_IsBanker DEFAULT (0);
ALTER TABLE settlement.Legs ADD Position int NOT NULL CONSTRAINT DF_SettlementLegs_Position DEFAULT (0);

CREATE TABLE settlement.Bets
(
    BetId     uniqueidentifier NOT NULL CONSTRAINT PK_SettlementBets PRIMARY KEY,
    CouponId  uniqueidentifier NOT NULL CONSTRAINT FK_SettlementBets_Coupons REFERENCES settlement.Coupons (CouponId),
    Folds     varchar(60)      NOT NULL,
    UnitStake bigint           NOT NULL
);
CREATE INDEX IX_SettlementBets_Coupon ON settlement.Bets (CouponId);
GO

-- Coupons indexed before V2 are one bet: every leg in one line, the whole stake on it.
INSERT INTO settlement.Bets (BetId, CouponId, Folds, UnitStake)
SELECT NEWID(), c.CouponId, CAST(c.LegCount AS varchar(10)), c.Stake
FROM settlement.Coupons c
WHERE NOT EXISTS (SELECT 1 FROM settlement.Bets b WHERE b.CouponId = c.CouponId);
