using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Infrastructure.Persistence;

public sealed class SqlSettlementStore(ISqlConnectionFactory connections, IOutbox outbox, IInboxStore inbox, TimeProvider time, IProgressCounter? progress = null) : ISettlementStore
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
        var rows = await connection.QueryAsync<(Guid LegId, Guid CouponId, string FixtureId, string MarketId, string SelectionId, decimal Odds, string ResultFixtureId, int ResultVersion, byte State, int HomeGoals, int AwayGoals, string? Builder)>(
            new CommandDefinition(Sql.Get("Settle.FindUnevaluated"), new { Limit = limit, Since = time.GetUtcNow().AddHours(-1) }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => (new IndexedLeg(r.LegId, r.CouponId, r.FixtureId, r.MarketId, r.SelectionId, r.Odds, Builder: r.Builder), new FixtureResult(r.ResultFixtureId, r.ResultVersion, (ResultState)r.State, r.HomeGoals, r.AwayGoals)))];
    }

    public async Task<IReadOnlyList<Application.Integrity.SettlementDigest>> GetSettlementDigestAsync(IReadOnlyList<Guid> couponIds, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid CouponId, int Version, byte Outcome, long Payout, DateTimeOffset SettledAt)>(
            new CommandDefinition(Sql.Get("Settle.IntegrityDigest"), new { Ids = couponIds }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => new Application.Integrity.SettlementDigest(r.CouponId, r.Version, (Domain.CouponOutcome)r.Outcome, r.Payout, r.SettledAt))];
    }

    public async Task<CouponStateView?> GetCouponStateAsync(Guid couponId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        using var reader = await connection.QueryMultipleAsync(new CommandDefinition(Sql.Get("Settle.CouponState"), new { CouponId = couponId }, cancellationToken: cancellationToken));
        var coupon = await reader.ReadSingleOrDefaultAsync<(Guid CouponId, int LegCount, bool SettlementPending, DateTimeOffset? LastEvaluatedAt)?>();
        if (coupon is not { } c)
        {
            return null;
        }

        var legs = (await reader.ReadAsync<(Guid LegId, string FixtureId, string SelectionId, decimal Odds, int? LatestResultVersion, byte? Outcome)>())
            .Select(l => new LegStateView(l.LegId, l.FixtureId, l.SelectionId, l.Odds, l.LatestResultVersion, l.Outcome is { } o ? ((LegOutcome)o).ToString() : null))
            .ToList();
        var settlements = (await reader.ReadAsync<(int Version, byte Outcome, long Payout, DateTimeOffset SettledAt)>())
            .Select(s => new SettlementStateView(s.Version, ((CouponOutcome)s.Outcome).ToString(), s.Payout, s.SettledAt))
            .ToList();
        var redis = progress is null ? -1 : await progress.ResolvedLegsAsync(couponId);
        return new CouponStateView(c.CouponId, c.LegCount, c.SettlementPending, c.LastEvaluatedAt, legs, settlements, redis);
    }
}
