using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Offer;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Consumers;

public sealed class ResultPublishedConsumer(EvaluateResultHandler handler) : IEventHandler<ResultPublishedV1>
{
    public Task HandleAsync(ConsumedEvent<ResultPublishedV1> message, CancellationToken cancellationToken) =>
        handler.HandleAsync(message.Envelope.Id, message.Envelope.Payload);
}
