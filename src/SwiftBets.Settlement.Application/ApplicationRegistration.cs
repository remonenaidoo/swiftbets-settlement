using Microsoft.Extensions.DependencyInjection;

namespace SwiftBets.Settlement.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddSettlementApplication(this IServiceCollection services) => services;
}
