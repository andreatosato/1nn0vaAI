using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

/// <summary>
/// Hosting shared by the three routers: Aspire service defaults, conversations, runs, SSE, exports and the
/// run worker. The router itself (instructions, tools, delegation) lives in its own project.
/// </summary>
public static class RouterHostApplication
{
    public static WebApplicationBuilder CreateBuilder<TRouter>(string[] args, DemoArchitecture architecture)
        where TRouter : class, IArchitectureRouter
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(architecture);
        builder.Services.AddSingleton<ObservatorySettings>();
        builder.Services.AddSingleton<EvidenceSanitizer>();
        builder.Services.AddSingleton<RemoteShopCatalog>();
        builder.Services.AddSingleton<IShopCatalog>(services => services.GetRequiredService<RemoteShopCatalog>());
        builder.Services.AddSingleton<EvidenceStore>();
        builder.Services.AddSingleton<RunCoordinator>();
        builder.Services.AddSingleton<ExperimentService>();
        builder.Services.AddAgentRuntime(builder.Configuration);
        builder.Services.AddSingleton<IArchitectureRouter, TRouter>();
        builder.Services.AddSingleton<IAgentRuntime>(services => services.GetRequiredService<IArchitectureRouter>());
        builder.Services.AddHostedService<RunWorker>();
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        return builder;
    }

    public static async Task<WebApplication> BuildAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        var catalog = app.Services.GetRequiredService<RemoteShopCatalog>();
        await catalog.InitializeAsync(app.Lifetime.ApplicationStopping);
        app.Logger.LogInformation(
            "Catalog loaded from the Catalog API: {ProductCount} products, {ScenarioCount} scenario definitions. Catalog hash: {CatalogHash}.",
            catalog.Products.Count, catalog.Scenarios.Count, catalog.Catalog.ContentHash);
        app.UseObservatoryErrors();
        app.MapDefaultEndpoints();
        app.MapObservatory();
        return app;
    }
}
