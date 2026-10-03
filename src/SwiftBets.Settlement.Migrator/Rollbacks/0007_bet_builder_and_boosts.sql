-- Rolls back 0007_bet_builder_and_boosts. Bet builder legs and boosted bets then settle as plain legs and bets.
ALTER TABLE settlement.Bets DROP CONSTRAINT IF EXISTS DF_SettlementBets_AccaBoostPercent;
ALTER TABLE settlement.Bets DROP COLUMN IF EXISTS AccaBoostPercent;
ALTER TABLE settlement.LegEvaluations DROP COLUMN IF EXISTS Odds;
ALTER TABLE settlement.Legs DROP COLUMN IF EXISTS Builder;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0007_bet_builder_and_boosts.sql';
