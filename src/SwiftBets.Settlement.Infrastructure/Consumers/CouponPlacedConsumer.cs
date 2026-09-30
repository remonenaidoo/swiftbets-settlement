using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Consumers;

public sealed class CouponPlacedConsumer(IndexCouponHandler handler) : IEventHandler<CouponPlacedV1>
{
    public Task HandleAsync(ConsumedEvent<CouponPlacedV1> message, CancellationToken cancellationToken) =>
        handler.HandleAsync(message.Envelope.Id, message.Envelope.Payload);
}
