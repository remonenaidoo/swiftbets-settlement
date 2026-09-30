using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Consumers;

public sealed class LegEvaluatedConsumer(SettleCouponHandler handler) : IEventHandler<LegEvaluatedV1>
{
    public Task HandleAsync(ConsumedEvent<LegEvaluatedV1> message, CancellationToken cancellationToken) =>
        handler.HandleAsync(message.Envelope.Payload);
}
