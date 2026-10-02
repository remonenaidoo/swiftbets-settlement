using Microsoft.Extensions.DependencyInjection;
using SwiftBets.Settlement.Application.Handlers;

namespace SwiftBets.Settlement.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddSettlementApplication(this IServiceCollection services)
    {
        services.AddScoped<IndexCouponHandler>();
        services.AddScoped<EvaluateResultHandler>();
        services.AddScoped<CouponSettler>();
        services.AddScoped<SettleCouponHandler>();
        services.AddScoped<ReconcileHandler>();
        services.AddScoped<ApplyManualResultHandler>();
        return services;
    }
}
