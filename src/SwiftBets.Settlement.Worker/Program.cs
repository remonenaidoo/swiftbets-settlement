using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Settlement.Application;
using SwiftBets.Settlement.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-settlement");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSettlementApplication();
builder.Services.AddSettlementInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.MapSwiftBetsOperationalEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "swiftbets-settlement" })).ExcludeFromDescription();

await app.RunAsync();
return 0;

public partial class Program;
