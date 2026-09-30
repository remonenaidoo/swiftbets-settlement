CREATE TABLE settlement.Coupons
(
    CouponId          uniqueidentifier  NOT NULL CONSTRAINT PK_SettlementCoupons PRIMARY KEY,
    PunterId          uniqueidentifier  NOT NULL,
    Stake             bigint            NOT NULL,
    Currency          char(3)           NOT NULL,
    LegCount          int               NOT NULL,
    SettlementPending bit               NOT NULL CONSTRAINT DF_SettlementCoupons_Pending DEFAULT (0),
    LastEvaluatedAt   datetimeoffset(3) NULL,
    IndexedAt         datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_SettlementCoupons_Pending ON settlement.Coupons (LastEvaluatedAt) WHERE SettlementPending = 1;

CREATE TABLE settlement.Legs
(
    LegId       uniqueidentifier NOT NULL CONSTRAINT PK_SettlementLegs PRIMARY KEY,
    CouponId    uniqueidentifier NOT NULL CONSTRAINT FK_SettlementLegs_Coupons REFERENCES settlement.Coupons (CouponId),
    FixtureId   nvarchar(100)    NOT NULL,
    MarketId    nvarchar(120)    NOT NULL,
    SelectionId nvarchar(50)     NOT NULL,
    Odds        decimal(10, 3)   NOT NULL
);
CREATE INDEX IX_SettlementLegs_Fixture ON settlement.Legs (FixtureId) INCLUDE (CouponId, SelectionId, Odds, MarketId);

CREATE TABLE settlement.Results
(
    FixtureId     nvarchar(100)     NOT NULL CONSTRAINT PK_Results PRIMARY KEY,
    ResultVersion int               NOT NULL,
    State         tinyint           NOT NULL,
    HomeGoals     int               NOT NULL,
    AwayGoals     int               NOT NULL,
    PublishedAt   datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_Results_PublishedAt ON settlement.Results (PublishedAt);

CREATE TABLE settlement.LegEvaluations
(
    LegId         uniqueidentifier  NOT NULL,
    ResultVersion int               NOT NULL,
    CouponId      uniqueidentifier  NOT NULL,
    Outcome       tinyint           NOT NULL,
    EvaluatedAt   datetimeoffset(3) NOT NULL,
    CONSTRAINT PK_LegEvaluations PRIMARY KEY (LegId, ResultVersion)
);
CREATE INDEX IX_LegEvaluations_Coupon ON settlement.LegEvaluations (CouponId) INCLUDE (ResultVersion, Outcome);

CREATE TABLE settlement.Settlements
(
    CouponId      uniqueidentifier  NOT NULL,
    Version       int               NOT NULL,
    Outcome       tinyint           NOT NULL,
    EffectiveOdds decimal(28, 6)    NOT NULL,
    Payout        bigint            NOT NULL,
    SettledAt     datetimeoffset(3) NOT NULL,
    CONSTRAINT PK_Settlements PRIMARY KEY (CouponId, Version)
);
