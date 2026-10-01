using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Placement;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Infrastructure.Consumers;

public sealed class CouponPlacedV2Consumer(IndexCouponHandler handler) : IEventHandler<CouponPlacedV2>
{
    public Task HandleAsync(ConsumedEvent<CouponPlacedV2> message, CancellationToken cancellationToken) =>
        handler.HandleAsync(message.Envelope.Id, message.Envelope.Payload);
}
