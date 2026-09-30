using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Infrastructure.Persistence;

public sealed class SqlSettlementStore(ISqlConnectionFactory connections, IOutbox outbox, IInboxStore inbox, TimeProvider time) : ISettlementStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlSettlementStore>();

    public async Task<ISettlementTransaction> BeginAsync()
    {
        var connection = await connections.OpenAsync(CancellationToken.None);
        return new SqlSettlementTransaction(connection, (SqlTransaction)await connection.BeginTransactionAsync(), outbox, inbox, time);
    }

    public async Task<IReadOnlyList<Guid>> FindUnsettledCouponsAsync(TimeSpan quietFor, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<Guid>(new CommandDefinition(Sql.Get("Settle.FindUnsettled"), new { Limit = limit, Cutoff = time.GetUtcNow() - quietFor }, cancellationToken: cancellationToken))];
    }

    public async Task<IReadOnlyList<(IndexedLeg Leg, FixtureResult Result)>> FindUnevaluatedLegsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds, string ResultFixtureId, int ResultVersion, byte State, int HomeGoals, int AwayGoals)>(
            new CommandDefinition(Sql.Get("Settle.FindUnevaluated"), new { Limit = limit, Since = time.GetUtcNow().AddHours(-1) }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => (new IndexedLeg(r.LegId, r.CouponId, r.FixtureId, r.MarketId, r.SelectionId, r.Odds), new FixtureResult(r.ResultFixtureId, r.ResultVersion, (ResultState)r.State, r.HomeGoals, r.AwayGoals)))];
    }
}
