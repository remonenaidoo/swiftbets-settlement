-- Every applied manual result, so a coupon indexed after it (its placement event was still in flight) gets it too.
CREATE TABLE settlement.ManualResults
(
    ManualResultId     uniqueidentifier  NOT NULL CONSTRAINT PK_ManualResults PRIMARY KEY,
    FixtureId          nvarchar(100)     NOT NULL,
    Scope              tinyint           NOT NULL,
    Action             tinyint           NOT NULL,
    MarketId           nvarchar(120)     NULL,
    CouponId           uniqueidentifier  NULL,
    WinningSelectionId nvarchar(50)      NULL,
    VoidFrom           datetimeoffset(3) NULL,
    Version            int               NOT NULL,
    IssuedAt           datetimeoffset(3) NOT NULL
);
CREATE INDEX IX_ManualResults_Fixture ON settlement.ManualResults (FixtureId, Version);
