namespace Observatory.Core;

public static class DemoTechnologies
{
    public const string Inline = "inline";
    public const string Skills = "skills";
    public const string A2A = "a2a";
    public static readonly string[] All = [Inline, Skills, A2A];
}

public static class DemoClock
{
    public static readonly DateTimeOffset AsOf = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    public const string CustomerId = "CUST-DEMO-01";
}

public sealed record Product
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int Stock { get; init; }
    public string? Brand { get; init; }
    public string Sku { get; init; } = "";
    public string? Thumbnail { get; init; }
    public string[] Images { get; init; } = [];
    public string[] Tags { get; init; } = [];

    public ProductFact ToFact() => new(Id, Title, Description, Category, Price, Currency, Stock, Brand, Sku);
}

public sealed record ProductFact(int Id, string Title, string Description, string Category,
    decimal Price, string Currency, int Stock, string? Brand, string Sku);

/// <summary>Filtri combinabili del catalogo; Take limita gli esempi, non i conteggi.</summary>
public sealed record CatalogQueryRequest
{
    public string? Query { get; init; }
    public string? Category { get; init; }
    public string? Color { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool InStockOnly { get; init; }
    public int Take { get; init; } = 5;
}

/// <summary>Conteggi sull'intero insieme filtrato ed esempi privi di immagini.</summary>
public sealed record CatalogQueryResponse(CatalogQueryRequest Filters, int TotalProducts, int InStockProducts,
    long StockUnits, IReadOnlyList<ProductFact> Products, bool HasMore, string ColorBasis);

/// <summary>Numero di modelli e pezzi associati a una categoria o a un colore testuale.</summary>
public sealed record CatalogFacet(string Value, int ProductCount, long StockUnits);

/// <summary>Categorie e colori effettivamente presenti; i colori possono sovrapporsi.</summary>
public sealed record CatalogFacetsResponse(int TotalProducts, IReadOnlyList<CatalogFacet> Categories,
    IReadOnlyList<CatalogFacet> Colors, string ColorBasis);

public sealed record CatalogSnapshot
{
    public string Source { get; init; } = "DummyJSON";
    public string SourceUrl { get; init; } = "";
    public DateTimeOffset RetrievedAt { get; init; }
    public string ContentHash { get; init; } = "";
    public string Notice { get; init; } = "Public sample catalog; prices and stock are not real commerce data.";
    public List<Product> Products { get; init; } = [];
}

public sealed record ShopOrder(string Id, string CustomerId, int ProductId, string ProductTitle,
    decimal ListPrice, decimal AmountPaid, string Currency, bool Outlet, DateOnly DeliveredAt,
    string Status, string TrackingCode);

public sealed record ShopPolicy(string Id, string Title, string Text, int Priority, string Version);

/// <summary>
/// Read-only teaching snapshot of synthetic orders and policies, including other demo customers.
/// This UI-only view grants no customer access to agent tools and never creates business or run state.
/// </summary>
/// <param name="AsOf">Frozen demo clock, including its UTC offset.</param>
/// <param name="CustomerId">Trusted chat customer; not a filter for this teaching snapshot.</param>
/// <param name="Notice">Explains the synthetic, cross-customer teaching scope and unchanged chat authorization.</param>
/// <param name="Orders">All synthetic system orders, with date-only delivery dates.</param>
/// <param name="Policies">All synthetic policies, including archived versions, highest priority first.</param>
public sealed record DemoDataResponse(DateTimeOffset AsOf, string CustomerId, string Notice,
    IReadOnlyList<ShopOrder> Orders, IReadOnlyList<ShopPolicy> Policies);
public sealed record ReturnAssessment(string OrderId, bool Eligible, string Reason,
    string PolicyId, int DaysSinceDelivery, decimal RefundAmount, string Currency, bool NeedsClarification = false);
public sealed record ReturnDraft(string Id, string OrderId, string Reason,
    decimal Amount, string Currency, string Status = "draft-synthetic");

public sealed record ScenarioTurn(string Message, string ExpectedIntent, string[] ExpectedFacts,
    bool ConfirmAction = false);
public sealed record ScenarioDefinition(string Id, string Name, string Group, string Split,
    IReadOnlyList<ScenarioTurn> Turns);

public interface IShopCatalog
{
    CatalogSnapshot Catalog { get; }
    IReadOnlyList<Product> Products { get; }
    IReadOnlyList<ScenarioDefinition> Scenarios { get; }
}

public interface IShopData : IShopCatalog
{
    /// <summary>All synthetic orders for the UI-only teaching inspector, never an agent tool.</summary>
    IReadOnlyList<ShopOrder> DemoOrders { get; }
    IReadOnlyList<ShopPolicy> Policies { get; }
    IReadOnlyList<ProductFact> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5);
    CatalogQueryResponse QueryCatalog(CatalogQueryRequest query);
    CatalogFacetsResponse GetCatalogFacets();
    ProductFact GetProduct(int productId);
    ShopOrder GetOrder(string orderId, string customerId = DemoClock.CustomerId);
    ReturnAssessment AssessReturn(string orderId, string reason, string customerId = DemoClock.CustomerId);
    ReturnDraft CreateReturnDraft(string orderId, string reason, bool confirmed,
        string customerId = DemoClock.CustomerId);
}

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record PriceDefinition
{
    public decimal? InputPerMillion { get; init; }
    public decimal? CachedInputPerMillion { get; init; }
    public decimal? OutputPerMillion { get; init; }
    public decimal? CacheWriteSurchargePerMillion { get; init; }
    public decimal? CacheWritePerMillion { get; init; }
    public long? LongContextThresholdTokens { get; init; }
    public decimal? LongContextInputPerMillion { get; init; }
    public decimal? LongContextCachedInputPerMillion { get; init; }
    public decimal? LongContextCacheWritePerMillion { get; init; }
    public decimal? LongContextOutputPerMillion { get; init; }
    public string Currency { get; init; } = "USD";
    public string? SourceUrl { get; init; }
    public DateOnly? VerifiedAt { get; init; }
    public string Version { get; init; } = "unconfigured";
}

public sealed record ModelDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Family { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string? ModelVersion { get; init; }
    public string? Deployment { get; init; }
    public string? Region { get; init; }
    public string? DeploymentType { get; init; }
    public bool Configured { get; init; }
    public PriceDefinition Pricing { get; init; } = new();
}

public static class ModelCatalog
{
    public static readonly ModelDefinition[] Defaults =
    [
        new() { Id = "gpt5", Name = "GPT-5", Family = "GPT-5", ModelId = "gpt-5" },
        new() { Id = "gpt6-astra", Name = "GPT-6 Astra", Family = "GPT-6", ModelId = "gpt-6-astra" },
        new() { Id = "gpt6-sol", Name = "GPT-6 Sol", Family = "GPT-6", ModelId = "gpt-6-sol" },
        new() { Id = "gpt6-luna", Name = "GPT-6 Luna", Family = "GPT-6", ModelId = "gpt-6-luna" }
    ];
}

public sealed record DemoConfiguration
{
    public string Technology { get; init; } = DemoTechnologies.Inline;
    public string DefaultMode { get; init; } = "live";
    public bool AllowLive { get; init; }
    public IReadOnlyList<ModelDefinition> Models { get; init; } = ModelCatalog.Defaults;
    public string[] PromptProfiles { get; init; } = ["bad", "good", "gpt5", "gpt6"];
    public string[] HistoryStrategies { get; init; } = ["full", "compact"];
    public DateTimeOffset AsOf { get; init; } = DemoClock.AsOf;
    public string DataNotice { get; init; } = "Catalogo pubblico DummyJSON; ordini e policy sintetici. Immagini solo nella UI.";
    public string? CatalogSourceUrl { get; init; }
    public DateTimeOffset? CatalogRetrievedAt { get; init; }
    public string? CatalogHash { get; init; }
}

public sealed record ChatMessageRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Role { get; init; } = "user";
    public string Text { get; init; } = "";
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string? RunId { get; init; }
    public IReadOnlyList<int> ProductIds { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record ConversationRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Technology { get; init; } = DemoTechnologies.Inline;
    public string Title { get; init; } = "Nuova conversazione";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ChatMessageRecord> Messages { get; init; } = [];
}

/// <summary>Optional instructional blocks; all disabled preserves the original prompt profile.</summary>
public sealed record PromptBlockSelection
{
    public bool Checklist { get; init; }
    public bool OutputContract { get; init; }
    public bool Examples { get; init; }
    public bool Redundancy { get; init; }
    public bool ConflictingStyle { get; init; }
}

public sealed record RunConfiguration
{
    public string Mode { get; init; } = "live";
    public string ModelProfileId { get; init; } = "gpt5";
    public Dictionary<string, string> AgentModels { get; init; } = [];
    public string PromptProfile { get; init; } = "good";
    public PromptBlockSelection PromptBlocks { get; init; } = new();
    public string HistoryStrategy { get; init; } = "full";
    public string ToolTransport { get; init; } = "direct";
    public bool ConfirmAction { get; init; }
    public int MaxOutputTokens { get; init; } = 1500;
    public int MaxModelCalls { get; init; } = 24;
    public decimal? ApprovedBudgetUsd { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool UnboundedExecution { get; init; }
}

public sealed record SubmitTurnRequest
{
    public string Message { get; init; } = "";
    public string IdempotencyKey { get; init; } = Guid.NewGuid().ToString("N");
    public RunConfiguration Configuration { get; init; } = new();
}

public sealed record AgentRunRequest
{
    public required string RunId { get; init; }
    public required string ConversationId { get; init; }
    public required string Technology { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<ChatMessageRecord> History { get; init; } = [];
    public RunConfiguration Configuration { get; init; } = new();
    public string CustomerId { get; init; } = DemoClock.CustomerId;
}

public sealed record AgentExecutionResult(string Answer, IReadOnlyList<int> ProductIds,
    IReadOnlyList<string> Sources, string? Decision = null);

public sealed record RunEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public long Sequence { get; init; }
    public required string RunId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public required string Kind { get; init; }
    public string Agent { get; init; } = "system";
    public string Message { get; init; } = "";
    public object? Data { get; init; }
}

public sealed record ModelCallRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string RunId { get; init; }
    public required string Agent { get; init; }
    public required string ModelProfileId { get; init; }
    public string Mode { get; init; } = "live";
    public string? ModelId { get; init; }
    public string? Deployment { get; init; }
    public string? ProviderResponseId { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public double DurationMs { get; init; }
    public long? InputTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long? CacheWriteTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? ReasoningTokens { get; init; }
    public string UsageSource { get; init; } = "unknown";
    public decimal? EstimatedCostUsd { get; init; }
    public string CostStatus { get; init; } = "unpriced";
    public string? PricingTier { get; init; }
    public string CaptureKind { get; init; } = "logical";
    public string Status { get; init; } = "completed";
    public object? Request { get; init; }
    public object? Response { get; init; }
    public object? RawUsage { get; init; }
    public IReadOnlyList<WireAttempt> Attempts { get; init; } = [];
    public string? Error { get; init; }
}

public sealed record WireAttempt
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? Endpoint { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
    public int? StatusCode { get; init; }
    public double DurationMs { get; init; }
    public bool Truncated { get; init; }
    public string? Error { get; init; }
}

public interface IAgentRuntime
{
    Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken = default);
}

public sealed record RunRecord
{
    public required string Id { get; init; }
    public required string ConversationId { get; init; }
    public required string Technology { get; init; }
    public string Status { get; init; } = "queued";
    public string Message { get; init; } = "";
    public RunConfiguration Configuration { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public double? DurationMs { get; init; }
    public double? TimeToFirstAnswerMs { get; init; }
    public AgentExecutionResult? Result { get; init; }
    public string? Error { get; init; }
    public List<RunEvent> Events { get; init; } = [];
    public List<ModelCallRecord> Calls { get; init; } = [];
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public decimal? EstimatedCostUsd { get; init; }
    public string CostStatus { get; init; } = "unpriced";
    public string? ScenarioId { get; init; }
    public string? ExperimentId { get; init; }
}

public sealed record ExperimentRequest
{
    public string[] ScenarioIds { get; init; } = ["main-six-turns"];
    public RunConfiguration[] Configurations { get; init; } = [new()];
    public int Repetitions { get; init; } = 1;
    public bool DryRun { get; init; } = true;
}

public sealed record EvaluationResult(string ScenarioId, int Turn, bool Passed,
    string[] MissingFacts, string[] ExpectedFacts, string Notice);
