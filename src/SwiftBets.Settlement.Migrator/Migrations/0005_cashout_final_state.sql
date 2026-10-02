-- A cashed-out coupon is final: FinalState is set under the coupon lock and no later result settles it again.
ALTER TABLE settlement.Coupons ADD FinalState tinyint NULL;

-- One row per accepted cashout; CashoutId makes the gRPC call idempotent and a coupon is cashed out at most once.
CREATE TABLE settlement.Cashouts
(
    CashoutId         uniqueidentifier  NOT NULL CONSTRAINT PK_SettlementCashouts PRIMARY KEY,
    CouponId          uniqueidentifier  NOT NULL CONSTRAINT FK_SettlementCashouts_Coupons REFERENCES settlement.Coupons (CouponId),
    Amount            bigint            NOT NULL CONSTRAINT CK_SettlementCashouts_Amount CHECK (Amount >= 0),
    Currency          char(3)           NOT NULL,
    SettlementVersion int               NOT NULL,
    CashedOutAt       datetimeoffset(3) NOT NULL,
    CONSTRAINT UQ_SettlementCashouts_Coupon UNIQUE (CouponId)
);
