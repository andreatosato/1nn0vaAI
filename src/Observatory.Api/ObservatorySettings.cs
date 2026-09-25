using System.Globalization;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

public sealed class ObservatorySettings
{
    private static readonly string[] Prompts = ["bad", "good", "gpt5", "gpt6"];
    private static readonly string[] Histories = ["full", "compact"];
    private static readonly string[] Transports = ["direct"];
    private readonly IConfiguration configuration;

    public ObservatorySettings(IConfiguration configuration)
    {
        this.configuration = configuration;
        Technology = configuration["Demo:Technology"] ?? DemoTechnologies.Inline;
        if (!DemoTechnologies.All.Contains(Technology, StringComparer.Ordinal))
            throw new InvalidOperationException("Demo:Technology must be inline, skills or a2a.");
        Workers = ReadRange("Processing:Workers", 2, 1, 16);
        QueueCapacity = ReadRange("Processing:QueueCapacity", 64, 1, 4096);
        MaxPendingRuns = ReadRange("Processing:MaxPendingRuns", 256, 1, 10000);
        RunTimeout = TimeSpan.FromSeconds(ReadRange("Processing:RunTimeoutSeconds", 180, 1, 1800));
        MaxApprovedBudgetUsd = configuration.GetValue<decimal?>("Demo:MaxApprovedBudgetUsd") ?? 10m;
        if (MaxApprovedBudgetUsd <= 0) throw new InvalidOperationException("Demo:MaxApprovedBudgetUsd must be positive.");
        if ((configuration["Demo:DefaultMode"] ?? "live") != "live")
            throw new InvalidOperationException("Demo:DefaultMode must be live.");
        DatabasePath = Path.GetFullPath(configuration["Storage:Path"] is { Length: > 0 } storage
            ? storage : Path.Combine(AppContext.BaseDirectory, "data", $"observatory-{Technology}.sqlite3"));
        Models = ModelCatalog.Defaults.Select(ReadModel).ToArray();
    }

    public string Technology { get; }
    public string DatabasePath { get; }
    public int Workers { get; }
    public int QueueCapacity { get; }
    public int MaxPendingRuns { get; }
    public TimeSpan RunTimeout { get; }
    public decimal MaxApprovedBudgetUsd { get; }
    public bool AllowUnboundedExecution => configuration.GetValue<bool>("Demo:AllowUnboundedExecution");
    public ModelDefinition[] Models { get; }
    public bool LiveEnabled => configuration.GetValue<bool?>("Demo:AllowLive")
        ?? configuration.GetValue<bool>("AllowLive");
    public bool AllowLive => LiveEnabled && Models.Any(m => m.Configured && FullyPriced(m.Pricing) && CapabilitiesVerified(m.Id));
    public string DefaultMode => "live";

    public DemoConfiguration Describe(IShopCatalog data) => new()
    {
        Technology = Technology,
        DefaultMode = DefaultMode,
        AllowLive = AllowLive,
        Models = Models,
        PromptProfiles = Prompts,
        HistoryStrategies = Histories,
        CatalogSourceUrl = data.Catalog.SourceUrl,
        CatalogRetrievedAt = data.Catalog.RetrievedAt,
        CatalogHash = data.Catalog.ContentHash
    };

    public object Capabilities => new
    {
        persistentRuns = true, persistentConversations = true, cancellation = true,
        sse = true, resumeEvents = true, storedEventReplay = true, sanitizedExport = true,
        experiments = true, dryRun = true, live = AllowLive,
        liveRequiresExplicitBudget = true, allowUnboundedExecution = AllowUnboundedExecution, providerUsageOnly = true,
        imageInference = false, benchmarkRanking = false,
        mcp = false, a2a = Technology == DemoTechnologies.A2A, skills = Technology == DemoTechnologies.Skills,
        toolTransports = Transports,
        historyStrategies = Histories,
        agentNames = AgentNames.ForTechnology(Technology),
        serviceNames = AgentNames.Specialists,
        businessApi = Technology != DemoTechnologies.A2A,
        executionTopology = Technology switch
        {
            DemoTechnologies.Inline => "router-http",
            DemoTechnologies.Skills => "router-skills-http",
            _ => "router-a2a"
        },
        modelCapabilities = Models.Select(m => new
        {
            modelProfileId = m.Id,
            functionCallingVerified = configuration.GetValue<bool>($"Models:{m.Id}:Capabilities:FunctionCalling"),
            maxOutputTokensVerified = configuration.GetValue<bool>($"Models:{m.Id}:Capabilities:MaxOutputTokens"),
            liveReady = LiveEnabled && m.Configured && FullyPriced(m.Pricing) && CapabilitiesVerified(m.Id)
        }),
        maxOutputTokens = 16384, maxModelCalls = 64,
        maxApprovedBudgetUsd = MaxApprovedBudgetUsd,
        replayNotice = "Replay reads stored events of the original run; it never executes a model."
    };

    public RunSnapshot Snapshot(SubmitTurnRequest request, string exactBody, IShopCatalog data) => new()
    {
        Request = request, ExactRequestBody = exactBody, Technology = Technology,
        Models = Models,
        Catalog = new(data.Catalog.Source, data.Catalog.SourceUrl, data.Catalog.RetrievedAt,
            data.Catalog.ContentHash, data.Products.Count, data.Catalog.Notice,
            "Product images are public catalog UI decorations only; no images enter agent histories or inference.")
    };

    public void Validate(SubmitTurnRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 12000)
            Invalid("invalid_message", "Message must contain between 1 and 12000 characters.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128 ||
            request.IdempotencyKey.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')))
            Invalid("invalid_idempotency_key", "IdempotencyKey must be 1–128 ASCII letters, digits, '-', '_', '.' or ':'.");
        if (request.Configuration is null) Invalid("invalid_configuration", "Configuration cannot be null.");
        ValidateConfiguration(request.Configuration!);
    }

    public void ValidateConfiguration(RunConfiguration value) => ValidateConfiguration(value, authorizeLive: true);

    public void ValidatePromptPreview(RunConfiguration value) => ValidateConfiguration(value, authorizeLive: false);

    private void ValidateConfiguration(RunConfiguration value, bool authorizeLive)
    {
        if (value.PromptBlocks is null) Invalid("invalid_prompt_blocks", "PromptBlocks must be an object, not null.");
        if (value.UnboundedExecution && !AllowUnboundedExecution)
            throw new ApiException(403, "unbounded_disabled", "Unbounded execution requires Demo:AllowUnboundedExecution=true on the backend.");
        if (value.UnboundedExecution && value.ApprovedBudgetUsd is not null)
            Invalid("conflicting_budget", "Unbounded execution requires ApprovedBudgetUsd=null; no monetary cap is applied.");
        if (value.Mode != "live") Invalid("invalid_mode", "Only LIVE model execution is supported.");
        if (!Prompts.Contains(value.PromptProfile)) Invalid("invalid_prompt_profile", "Unknown prompt profile.");
        if (!Histories.Contains(value.HistoryStrategy)) Invalid("invalid_history_strategy", "Unknown history strategy.");
        if (!Transports.Contains(value.ToolTransport)) Invalid("invalid_tool_transport", "Only direct tool transport is implemented. MCP is unavailable.");
        if (value.MaxModelCalls is < 1 or > 64) Invalid("invalid_call_limit", "MaxModelCalls must be between 1 and 64.");
        if (value.MaxOutputTokens is < 1 or > 16384) Invalid("invalid_output_limit", "MaxOutputTokens must be between 1 and 16384.");
        if (value.AgentModels is null || value.AgentModels.Count > 16)
            Invalid("invalid_agent_models", "AgentModels must be an object with at most 16 known agent names.");
        var knownAgents = AgentNames.ForTechnology(Technology);
        foreach (var name in value.AgentModels!.Keys)
            if (!knownAgents.Contains(name, StringComparer.Ordinal))
                Invalid("invalid_agent_name", $"Agent '{name}' is not active for {Technology}.");
        foreach (var id in new[] { value.ModelProfileId }.Concat(value.AgentModels.Values).Distinct())
            if (!Models.Any(m => m.Id == id)) Invalid("invalid_model_profile", $"Unknown model profile '{id}'.");
        var selected = knownAgents.Select(agent => value.AgentModels.GetValueOrDefault(agent, value.ModelProfileId)).Distinct().ToArray();
        if (value.ApprovedBudgetUsd is <= 0 || value.ApprovedBudgetUsd > MaxApprovedBudgetUsd)
            Invalid("invalid_budget", $"ApprovedBudgetUsd must be positive and at most {MaxApprovedBudgetUsd.ToString(CultureInfo.InvariantCulture)}.");
        if (authorizeLive && value.Mode == "live")
        {
            if (!LiveEnabled)
                throw new ApiException(403, "live_disabled", "LIVE is disabled by the backend; no provider request was made.");
            if (!value.UnboundedExecution && value.ApprovedBudgetUsd is null)
                throw new ApiException(422, "budget_required", "LIVE requires an explicit positive ApprovedBudgetUsd for this run.");
            foreach (var id in selected)
            {
                var model = Models.Single(m => m.Id == id);
                if (!model.Configured)
                    throw new ApiException(422, "deployment_required", $"Model '{id}' requires AzureOpenAI:Endpoint and Models:{id}:Deployment.");
                if (!FullyPriced(model.Pricing))
                    throw new ApiException(422, "pricing_required", $"Model '{id}' requires verified USD input, cached-input and output prices before LIVE.");
                if (!CapabilitiesVerified(id))
                    throw new ApiException(422, "capabilities_required", $"Model '{id}' requires explicitly verified FunctionCalling and MaxOutputTokens capabilities.");
            }
        }
    }

    public static string ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var guid))
            throw new ApiException(400, "invalid_id", "Identifiers must be 32 hexadecimal characters.");
        return guid.ToString("N");
    }

    public static bool FullyPriced(PriceDefinition price) =>
        TokenCostCalculator.HasCompleteRates(price) &&
        !string.IsNullOrWhiteSpace(price.SourceUrl) && price.VerifiedAt is not null;

    private ModelDefinition ReadModel(ModelDefinition model)
    {
        var section = configuration.GetSection($"Models:{model.Id}");
        var priceSection = section.GetSection("Pricing");
        string? Read(string key) => section[key] ?? priceSection[key];
        decimal? Price(string key)
        {
            var raw = Read(key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new InvalidOperationException($"Models:{model.Id}:{key} must be a nonnegative decimal.");
            return value;
        }
        DateOnly? verified = null;
        if (Read("VerifiedAt") is { Length: > 0 } date)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new InvalidOperationException($"Models:{model.Id}:VerifiedAt must be YYYY-MM-DD.");
            verified = parsed;
        }
        var source = Read("SourceUrl");
        if (!string.IsNullOrWhiteSpace(source) && !IsSafeHttps(source))
            throw new InvalidOperationException($"Models:{model.Id}:SourceUrl must be HTTPS without credentials, query or fragment.");
        var deployment = section["Deployment"];
        long? threshold = null;
        if (Read("LongContextThresholdTokens") is { Length: > 0 } boundary)
        {
            if (!long.TryParse(boundary, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                throw new InvalidOperationException($"Models:{model.Id}:LongContextThresholdTokens must be a positive integer.");
            threshold = parsed;
        }
        if (deployment is { Length: > 0 } && (deployment.Length > 128 ||
            deployment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new InvalidOperationException($"Models:{model.Id}:Deployment is invalid.");
        return model with
        {
            Deployment = deployment,
            ModelVersion = section["ModelVersion"],
            Region = section["Region"],
            DeploymentType = section["DeploymentType"],
            Configured = !string.IsNullOrWhiteSpace(deployment) && IsSafeHttps(configuration["AzureOpenAI:Endpoint"]),
            Pricing = new()
            {
                InputPerMillion = Price("InputPerMillion"),
                CachedInputPerMillion = Price("CachedInputPerMillion"),
                OutputPerMillion = Price("OutputPerMillion"),
                CacheWriteSurchargePerMillion = Price("CacheWriteSurchargePerMillion"),
                CacheWritePerMillion = Price("CacheWritePerMillion"),
                LongContextThresholdTokens = threshold,
                LongContextInputPerMillion = Price("LongContextInputPerMillion"),
                LongContextCachedInputPerMillion = Price("LongContextCachedInputPerMillion"),
                LongContextCacheWritePerMillion = Price("LongContextCacheWritePerMillion"),
                LongContextOutputPerMillion = Price("LongContextOutputPerMillion"),
                Currency = Read("Currency") ?? "USD",
                SourceUrl = source, VerifiedAt = verified, Version = Read("Version") ?? "unconfigured"
            }
        };
    }

    private static bool IsSafeHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private bool CapabilitiesVerified(string id) =>
        configuration.GetValue<bool>($"Models:{id}:Capabilities:FunctionCalling") &&
        configuration.GetValue<bool>($"Models:{id}:Capabilities:MaxOutputTokens");

    private int ReadRange(string name, int fallback, int minimum, int maximum)
    {
        var result = configuration.GetValue<int?>(name) ?? fallback;
        return result >= minimum && result <= maximum ? result
            : throw new InvalidOperationException($"{name} must be between {minimum} and {maximum}.");
    }

    private static void Invalid(string code, string message) => throw new ApiException(400, code, message);
}
