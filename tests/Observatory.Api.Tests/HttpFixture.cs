using Observatory.Core;

namespace Observatory.Api;

internal static class HttpFixture
{
    public static async Task Run(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (string.IsNullOrWhiteSpace(builder.Configuration["Storage:Path"]))
            throw new InvalidOperationException("HTTP fixture requires an explicit project-local Storage:Path.");
        builder.Configuration["Demo:AllowLive"] = "false";
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddSingleton<ObservatorySettings>();
        builder.Services.AddSingleton<EvidenceSanitizer>();
        builder.Services.AddSingleton<IShopData, FixtureData>();
        builder.Services.AddSingleton<IShopCatalog>(services => services.GetRequiredService<IShopData>());
        builder.Services.AddSingleton<IAgentRuntime, FixtureRuntime>();
        builder.Services.AddSingleton<EvidenceStore>();
        builder.Services.AddSingleton<RunCoordinator>();
        builder.Services.AddSingleton<ExperimentService>();
        builder.Services.AddHostedService<RunWorker>();
        var app = builder.Build();
        app.UseObservatoryErrors();
        app.MapHealthChecks("/health");
        app.MapObservatory();
        await app.RunAsync();
    }

    private sealed class FixtureRuntime : IAgentRuntime
    {
        public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit, CancellationToken cancellationToken = default)
        {
            if (request.Configuration.Mode != "live") throw new InvalidOperationException("Fixture accepts only LIVE-shaped requests.");
            await emit(new() { RunId = request.RunId, Kind = "agent.started", Agent = "router", Message = "Test fixture, not production runtime." });
            await Task.Delay(request.Message == "hold" ? 10000 : 40, cancellationToken);
            await emit(new()
            {
                RunId = request.RunId, Kind = "model.completed",
                Data = new ModelCallRecord
                {
                    RunId = request.RunId, Agent = "router", ModelProfileId = request.Configuration.ModelProfileId,
                    UsageSource = "fixture", Mode = "live", CaptureKind = "logical",
                    Request = new { request.Message, request.History }, Response = new { text = "fixture: " + request.Message }
                }
            });
            if (request.Message == "fail") throw new InvalidOperationException("Intentional HTTP fixture failure.");
            var answer = "fixture: " + request.Message;
            await emit(new() { RunId = request.RunId, Kind = "answer.delta", Agent = "router", Message = answer, Data = new { text = answer } });
            await emit(new() { RunId = request.RunId, Kind = "agent.completed", Agent = "router" });
            return new(answer, [83], ["fixture"]);
        }
    }

    private sealed class FixtureData : IShopData
    {
        public CatalogSnapshot Catalog { get; } = new()
        {
            Source = "HTTP self-check fixture", SourceUrl = "https://example.invalid/catalog?limit=1",
            Products = [new() { Id = 83, Title = "Fixture product", Price = 29.99m, Stock = 1 }]
        };
        public IReadOnlyList<Product> Products => Catalog.Products;
        public IReadOnlyList<ShopOrder> DemoOrders => [];
        public IReadOnlyList<ShopPolicy> Policies => [];
        public IReadOnlyList<ScenarioDefinition> Scenarios { get; } =
        [
            new("main-six-turns", "Fixture smoke", "fixture", "test",
                Enumerable.Range(1, 6).Select(i => new ScenarioTurn($"turn {i}", "fixture", ["fixture"])).ToArray()),
            new("failure-case", "Fixture failures", "fixture", "test",
                [new("fail", "fixture", ["fixture"]), new("another turn", "fixture", ["intentionally-missing"])])
        ];
        public IReadOnlyList<ProductFact> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5) => Products.Select(p => p.ToFact()).ToArray();
        public CatalogQueryResponse QueryCatalog(CatalogQueryRequest query) => throw new NotSupportedException("Fixture has no structured catalog tools.");
        public CatalogFacetsResponse GetCatalogFacets() => throw new NotSupportedException("Fixture has no catalog facets.");
        public ProductFact GetProduct(int productId) => Products.Single(p => p.Id == productId).ToFact();
        public ShopOrder GetOrder(string orderId, string customerId = DemoClock.CustomerId) => throw new NotSupportedException("Fixture has no order tools.");
        public ReturnAssessment AssessReturn(string orderId, string reason, string customerId = DemoClock.CustomerId) => throw new NotSupportedException("Fixture has no return tools.");
        public ReturnDraft CreateReturnDraft(string orderId, string reason, bool confirmed, string customerId = DemoClock.CustomerId) => throw new NotSupportedException("Fixture has no return tools.");
    }
}
