using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Testing;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace SwiftBets.Settlement.Migrator.Tests;

public sealed class MigratorTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Migrator_creates_the_schema_and_is_idempotent()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] args = [$"--ConnectionStrings:SbSettlement={connectionString}"];

        (await RunAsync(args)).ShouldBe(0);
        (await RunAsync(args)).ShouldBe(0);

        await using var connection = new SqlConnection(connectionString);
        var tables = (await connection.QueryAsync<string>("SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id")).ToList();
        tables.ShouldContain("inbox.ProcessedMessages");
        tables.ShouldContain("outbox.Messages");
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name = 'settlement'")).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_a_usage_code() =>
        (await RunAsync([])).ShouldBe(2);

    private static async Task<int> RunAsync(string[] args)
    {
        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        var result = entryPoint.Invoke(null, [args]);
        return result is Task<int> task ? await task : (int)result!;
    }
}
