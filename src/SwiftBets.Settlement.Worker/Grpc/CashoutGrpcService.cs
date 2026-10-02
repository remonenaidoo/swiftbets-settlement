using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Grpc.Settlement.Cashout.V1;
using SwiftBets.Settlement.Application.Handlers;
using DomainLegOutcome = SwiftBets.Settlement.Domain.LegOutcome;
using CashoutBase = SwiftBets.Contracts.Grpc.Settlement.Cashout.V1.Cashout.CashoutBase;

namespace SwiftBets.Settlement.Worker.Grpc;

/// <summary>Settlement's side of cashout, for the cashout service only. Expected refusals are typed replies, not status codes.</summary>
[Authorize(Policy = Roles.Service)]
public sealed class CashoutGrpcService(CashoutStateQuery stateQuery, CashOutHandler cashOut) : CashoutBase
{
    public override async Task<CashoutState> GetCashoutState(GetCashoutStateRequest request, ServerCallContext context)
    {
        var state = await stateQuery.GetAsync(Id(request.CouponId)) ?? throw new RpcException(new Status(StatusCode.NotFound, "coupon_not_found"));
        var reply = new CashoutState
        {
            CouponId = state.CouponId.ToString(),
            PunterId = state.PunterId.ToString(),
            Stake = new Money { MinorUnits = state.Stake, Currency = state.Currency },
            State = state.Coupon switch
            {
                CashoutStateQuery.CouponState.Open => CouponState.Open,
                CashoutStateQuery.CouponState.Settled => CouponState.Settled,
                _ => CouponState.CashedOut,
            },
            SingleLine = state.SingleLine,
        };
        reply.Legs.AddRange(state.Legs.Select(l => new CashoutLeg
        {
            LegId = l.LegId.ToString(),
            FixtureId = l.FixtureId,
            MarketId = l.MarketId,
            SelectionId = l.SelectionId,
            PlacedOdds = l.Odds.ToString(CultureInfo.InvariantCulture),
            IsBanker = l.IsBanker,
            State = l.Outcome switch
            {
                null => LegState.Open,
                DomainLegOutcome.Won => LegState.Won,
                DomainLegOutcome.Lost => LegState.Lost,
                _ => LegState.Void,
            },
        }));
        return reply;
    }

    public override async Task<CashOutReply> CashOut(CashOutRequest request, ServerCallContext context)
    {
        var amount = request.Amount ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "amount is required"));
        var reply = await cashOut.HandleAsync(new CashOutHandler.Request(Id(request.CashoutId), Id(request.CouponId), Id(request.PunterId), amount.MinorUnits, amount.Currency));
        return new CashOutReply
        {
            Status = reply.Accepted ? CashOutStatus.Accepted : CashOutStatus.Refused,
            RefusalCode = reply.RefusalCode ?? string.Empty,
            SettlementVersion = reply.SettlementVersion,
            WasApplied = reply.WasApplied,
        };
    }

    private static Guid Id(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{value}' is not an id"));
}
