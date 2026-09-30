using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Redis;

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
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
