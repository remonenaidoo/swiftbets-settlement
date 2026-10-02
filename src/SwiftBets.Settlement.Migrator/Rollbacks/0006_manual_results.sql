-- Rolls back 0006_manual_results. Coupons indexed after a manual result stop receiving it until this is reapplied.
DROP TABLE IF EXISTS settlement.ManualResults;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0006_manual_results.sql';
