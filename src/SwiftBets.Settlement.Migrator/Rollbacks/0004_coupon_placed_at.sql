-- Rolls back 0004_coupon_placed_at. Placement times recorded since are lost; time-voids stop working until it is reapplied.
ALTER TABLE settlement.Coupons DROP COLUMN IF EXISTS PlacedAt;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0004_coupon_placed_at.sql';
