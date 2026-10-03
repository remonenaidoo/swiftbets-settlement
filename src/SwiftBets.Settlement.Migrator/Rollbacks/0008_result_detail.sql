-- Rolls back 0008_result_detail. Results lose tennis games and winners; affected legs need their results republished.
ALTER TABLE settlement.Results DROP COLUMN HomeGames, AwayGames, Winners;
DELETE FROM dbo.SchemaVersions WHERE ScriptName = 'SwiftBets.Settlement.Migrator.Migrations.0008_result_detail.sql';
