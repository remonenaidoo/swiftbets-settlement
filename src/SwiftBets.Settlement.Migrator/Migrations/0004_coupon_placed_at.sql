-- When each coupon was placed, from the indexed placement event; time-voids compare it with the trader's cut-off.
-- NULL for coupons indexed before this migration: their placement time was never stored.
ALTER TABLE settlement.Coupons ADD PlacedAt datetimeoffset(3) NULL;
