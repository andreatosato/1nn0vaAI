using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

public static class DemoApiApplication
{
public static WebApplicationBuilder CreateBuilder(string[] args, string technology)
{
var builder = WebApplication.CreateBuilder(args);
builder.Configuration["Demo:Technology"] = technology;
builder.AddServiceDefaults();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ObservatorySettings>();
builder.Services.AddSingleton<EvidenceSanitizer>();
builder.Services.AddSingleton<RemoteShopCatalog>();
builder.Services.AddSingleton<IShopCatalog>(services => services.GetRequiredService<RemoteShopCatalog>());
builder.Services.AddSingleton<EvidenceStore>();
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddSingleton<ExperimentService>();
builder.Services.AddObservatoryAgents(builder.Configuration);
builder.Services.AddHostedService<RunWorker>();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
return builder;
}

public static async Task<WebApplication> BuildAsync(WebApplicationBuilder builder)
{
var app = builder.Build();
var shopData = app.Services.GetRequiredService<RemoteShopCatalog>();
await shopData.InitializeAsync(app.Lifetime.ApplicationStopping);
app.Logger.LogInformation(
    "Catalog loaded from Catalog service: {ProductCount} products, {ScenarioCount} scenario definitions. Catalog hash: {CatalogHash}. No conversation or run generated.",
    shopData.Products.Count, shopData.Scenarios.Count, shopData.Catalog.ContentHash);
app.UseObservatoryErrors();
app.MapDefaultEndpoints();
app.MapObservatory();
return app;
}
}
