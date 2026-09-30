using SwiftBets.BuildingBlocks.Core;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Offer;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Domain;

namespace SwiftBets.Settlement.Application.Handlers;

/// <summary>
/// Stage one. A result passes the priority gate only if it outranks the stored one; a duplicate or stale result is a
/// no-op. Each affected leg is evaluated and a leg-evaluated event is written to the outbox in the same transaction.
/// </summary>
public sealed class EvaluateResultHandler(ISettlementStore store, IFaultPoint faults, TimeProvider time)
{
    public const string Consumer = "settlement.evaluator";
    public const string FaultAfterCommit = "settlement.evaluator.after-commit";

    public async Task<int> HandleAsync(Guid eventId, ResultPublishedV1 published)
    {
        var result = new FixtureResult(published.FixtureId, published.ResultVersion, SettlementMapping.State(published.Status), published.HomeGoals, published.AwayGoals);
        var evaluated = 0;
        await using (var transaction = await store.BeginAsync())
        {
            if (!await transaction.TryRecordInboxAsync(Consumer, eventId))
            {
                return 0;
            }

            if (!result.Outranks(await transaction.LockResultAsync(result.FixtureId)))
            {
                await transaction.CommitAsync();
                return 0;
            }

            await transaction.SaveResultAsync(result);
            if (result.IsSettleable)
            {
                foreach (var leg in await transaction.GetLegsForFixtureAsync(result.FixtureId))
                {
                    var outcome = LegRules.Evaluate(leg.SelectionId, result);
                    if (await transaction.TryInsertEvaluationAsync(leg, result.Version, outcome))
                    {
                        await transaction.EnqueueAsync(Topics.LegEvaluated, leg.CouponId.ToString(),
                            new LegEvaluatedV1(leg.CouponId, leg.LegId, leg.FixtureId, result.Version, SettlementMapping.Map(outcome), time.GetUtcNow()));
                        evaluated++;
                    }
                }
            }

            await transaction.CommitAsync();
        }

        await faults.HitAsync(FaultAfterCommit);
        return evaluated;
    }
}
