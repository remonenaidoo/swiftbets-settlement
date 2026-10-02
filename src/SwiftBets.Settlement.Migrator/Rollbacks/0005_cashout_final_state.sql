-- Rolls back 0005_cashout_final_state. Cashed-out coupons lose their lock and could be resettled by a later result;
-- run only with cashout switched off and no cashed-out coupon still open to results.
DROP TABLE IF EXISTS settlement.Cashouts;
ALTER TABLE settlement.Coupons DROP COLUMN IF EXISTS FinalState;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0005_cashout_final_state.sql';
