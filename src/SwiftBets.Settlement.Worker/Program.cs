using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Settlement.Application;
using SwiftBets.Settlement.Application.Handlers;
using SwiftBets.Settlement.Application.Ports;
using SwiftBets.Contracts.Errors;
using SwiftBets.Contracts.Serialization;
using SwiftBets.Settlement.Infrastructure;
using SwiftBets.Settlement.Worker.Grpc;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-settlement");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddGrpc(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddSettlementApplication();
builder.Services.AddSettlementInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapGet("/coupons/{couponId:guid}/state", async (Guid couponId, ISettlementStore store, HttpContext context, CancellationToken cancellationToken) =>
        await store.GetCouponStateAsync(couponId, cancellationToken) is { } state
            ? Results.Json(state, ContractJson.Options)
            : Error.NotFound("coupon_not_found", "Settlement has not indexed that coupon.").ToHttpResult(context))
    .RequireAuthorization(Roles.OperatorOrService);
app.MapGrpcService<CashoutGrpcService>();
app.MapSwiftBetsFaultEndpoints();
app.MapPost("/coupons/{couponId:guid}/refresh", async (Guid couponId, ReconcileHandler reconciler) =>
        Results.Ok(new { couponId, resettled = await reconciler.RepairAsync(couponId, "operator_refresh") }))
    .RequireAuthorization(Roles.OperatorOrService);

await app.RunAsync();
return 0;

public partial class Program;
