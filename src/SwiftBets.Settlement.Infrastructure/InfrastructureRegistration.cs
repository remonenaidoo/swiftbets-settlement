using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Redis;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Offer;
using SwiftBets.Contracts.Placement;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Settlement.Infrastructure.Consumers;
using SwiftBets.Settlement.Infrastructure.Persistence;
using SwiftBets.Settlement.Infrastructure.Redis;
using SwiftBets.Settlement.Infrastructure.Workers;

namespace SwiftBets.Settlement.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddSettlementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSqlServerPersistence(Required(configuration, "ConnectionStrings:SbSettlement"));
        services.AddKafkaMessaging(configuration);
        services.AddSqlServerOutbox(configuration);
        services.AddSwiftBetsRedis(Required(configuration, "ConnectionStrings:Redis"));
        services.AddFaultInjection(configuration);
        services.AddValidatedOptions<ReconcilerOptions>(configuration, ReconcilerOptions.SectionName);
        services.AddSingleton<ISettlementStore, SqlSettlementStore>();
        services.AddSingleton<IProgressCounter, RedisProgressCounter>();

        if (configuration.GetValue("Settlement:RunConsumers", true))
        {
            services.AddKafkaConsumer<CouponPlacedV1, CouponPlacedConsumer>(Topics.CouponPlaced, "swiftbets.settlement.indexer");
            services.AddKafkaConsumer<ResultPublishedV1, ResultPublishedConsumer>(Topics.ResultPublished, "swiftbets.settlement.evaluator");
            services.AddKafkaConsumer<LegEvaluatedV1, LegEvaluatedConsumer>(Topics.LegEvaluated, "swiftbets.settlement.settler");
            services.AddHostedService<ReconcilerWorker>();
        }

        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
