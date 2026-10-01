-- Rolls back 0003_bets_and_bankers. Run only while system bets are off: a coupon with bankers or several bets loses its shape.
DROP TABLE IF EXISTS settlement.Bets;
ALTER TABLE settlement.Legs DROP CONSTRAINT IF EXISTS DF_SettlementLegs_IsBanker;
ALTER TABLE settlement.Legs DROP CONSTRAINT IF EXISTS DF_SettlementLegs_Position;
ALTER TABLE settlement.Legs DROP COLUMN IF EXISTS IsBanker, Position;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0003_bets_and_bankers.sql';
