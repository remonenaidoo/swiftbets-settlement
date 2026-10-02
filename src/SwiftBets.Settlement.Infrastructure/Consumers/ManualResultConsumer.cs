using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Trading;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Consumers;

public sealed class ManualResultConsumer(ApplyManualResultHandler handler) : IEventHandler<ManualResultV1>
{
    public Task HandleAsync(ConsumedEvent<ManualResultV1> message, CancellationToken cancellationToken) => handler.HandleAsync(message.Envelope.Payload);
}
