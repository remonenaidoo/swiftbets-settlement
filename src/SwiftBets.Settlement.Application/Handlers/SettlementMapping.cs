using SwiftBets.Contracts.Offer;
using DomainOutcome = SwiftBets.Settlement.Domain.CouponOutcome;
using DomainLegOutcome = SwiftBets.Settlement.Domain.LegOutcome;
using DomainState = SwiftBets.Settlement.Domain.ResultState;

namespace SwiftBets.Settlement.Application.Handlers;

internal static class SettlementMapping
{
    public static DomainState State(ResultStatus status) => status switch
    {
        ResultStatus.Provisional => DomainState.Provisional,
        ResultStatus.Official => DomainState.Official,
        ResultStatus.Correction => DomainState.Correction,
        _ => DomainState.Void,
    };

    public static Contracts.Settlement.LegOutcome Map(DomainLegOutcome outcome) => outcome switch
    {
        DomainLegOutcome.Won => Contracts.Settlement.LegOutcome.Won,
        DomainLegOutcome.Lost => Contracts.Settlement.LegOutcome.Lost,
        _ => Contracts.Settlement.LegOutcome.Void,
    };

    public static Contracts.Settlement.CouponOutcome Map(DomainOutcome outcome) => outcome switch
    {
        DomainOutcome.Won => Contracts.Settlement.CouponOutcome.Won,
        DomainOutcome.Lost => Contracts.Settlement.CouponOutcome.Lost,
        _ => Contracts.Settlement.CouponOutcome.Void,
    };
}
