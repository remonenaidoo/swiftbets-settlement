using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Settlement.Application;
using SwiftBets.Settlement.Application.Handlers;
using SwiftBets.Settlement.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-settlement");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddSettlementApplication();
builder.Services.AddSettlementInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapPost("/coupons/{couponId:guid}/refresh", async (Guid couponId, ReconcileHandler reconciler) =>
        Results.Ok(new { couponId, resettled = await reconciler.RepairAsync(couponId, "operator_refresh") }))
    .RequireAuthorization(Roles.Operator);

await app.RunAsync();
return 0;

public partial class Program;
