using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>Cashout v1 covers singles and accumulators: one bet whose only fold takes every non-banker leg.</summary>
public static class CashoutRules
{
    public static bool IsSingleLine(IReadOnlyList<SettlementBet> bets, int nonBankerLegs) =>
        bets is [{ Folds: [var fold] }] && fold == nonBankerLegs;
}
