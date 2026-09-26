using System.Globalization;
using Microsoft.Extensions.Configuration;
using Observatory.Core;

namespace Observatory.AgentRuntime;

public sealed record ModelRegistration(ModelDefinition Model, string Provider, bool FunctionCallingVerified,
    bool MaxOutputTokensVerified, string[] SentParameters, string PromptNotice)
{
    public bool UseNonReasoningChatTools { get; init; }
}

public sealed class AgentModelRegistry(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;
    public bool AllowLive => Configuration.GetValue<bool?>("Demo:AllowLive")
        ?? Configuration.GetValue<bool>("AllowLive");
    public IReadOnlyList<ModelRegistration> Registrations => ModelCatalog.Defaults.Select(model =>
    {
        var section = Configuration.GetSection($"Models:{model.Id}");
        var deployment = section["Deployment"];
        return new ModelRegistration(model with
        {
            Deployment = deployment,
            ModelVersion = section["ModelVersion"],
            Region = section["Region"],
            DeploymentType = section["DeploymentType"],
            Configured = !string.IsNullOrWhiteSpace(deployment) && !string.IsNullOrWhiteSpace(Configuration["AzureOpenAI:Endpoint"]),
            Pricing = ReadPricing(section)
        }, "AzureOpenAI.ChatCompletions",
            section.GetValue<bool>("Capabilities:FunctionCalling"),
            section.GetValue<bool>("Capabilities:MaxOutputTokens"),
            ["ChatOptions.Tools", "ChatOptions.MaxOutputTokens", "ChatOptions.AllowMultipleToolCalls"],
            "Model-specific profiles are unoptimized controls; no model-specific guide/capability is inferred from an alias.")
        {
            UseNonReasoningChatTools = section.GetValue<bool>("UseNonReasoningChatTools")
        };
    }).ToArray();

    private static PriceDefinition ReadPricing(IConfigurationSection model)
    {
        var nested = model.GetSection("Pricing");
        string? Read(string key) => model[key] ?? nested[key];
        decimal? Rate(string key)
        {
            var text = Read(key);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new DomainException("invalid_pricing_configuration", $"Models:{model.Key}:{key} deve essere un decimale non negativo.");
            return value;
        }
        DateOnly? verifiedAt = null;
        if (Read("VerifiedAt") is { Length: > 0 } date)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new DomainException("invalid_pricing_configuration", $"Models:{model.Key}:VerifiedAt deve avere formato yyyy-MM-dd.");
            verifiedAt = parsed;
        }
        var source = Read("SourceUrl");
        long? threshold = null;
        if (Read("LongContextThresholdTokens") is { Length: > 0 } boundary)
        {
            if (!long.TryParse(boundary, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                throw new DomainException("invalid_pricing_configuration", $"Models:{model.Key}:LongContextThresholdTokens deve essere un intero positivo.");
            threshold = parsed;
        }
        if (!string.IsNullOrWhiteSpace(source)
            && (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
            throw new DomainException("invalid_pricing_configuration", $"Models:{model.Key}:SourceUrl deve usare HTTPS senza credenziali, query o fragment.");
        return new PriceDefinition
        {
            InputPerMillion = Rate("InputPerMillion"),
            CachedInputPerMillion = Rate("CachedInputPerMillion"),
            OutputPerMillion = Rate("OutputPerMillion"),
            CacheWriteSurchargePerMillion = Rate("CacheWriteSurchargePerMillion"),
            CacheWritePerMillion = Rate("CacheWritePerMillion"),
            LongContextThresholdTokens = threshold,
            LongContextInputPerMillion = Rate("LongContextInputPerMillion"),
            LongContextCachedInputPerMillion = Rate("LongContextCachedInputPerMillion"),
            LongContextCacheWritePerMillion = Rate("LongContextCacheWritePerMillion"),
            LongContextOutputPerMillion = Rate("LongContextOutputPerMillion"),
            Currency = Read("Currency") ?? "USD",
            SourceUrl = source,
            VerifiedAt = verifiedAt,
            Version = Read("Version") ?? "unconfigured"
        };
    }

    internal ModelRegistration ForAgent(AgentRunRequest request, string agent)
    {
        var id = request.Configuration.AgentModels.GetValueOrDefault(agent, request.Configuration.ModelProfileId);
        return Registrations.SingleOrDefault(model => model.Model.Id == id)
            ?? throw new DomainException("unknown_model", $"Profilo modello non registrato: {id}.");
    }

    public void Validate(AgentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Configuration is null || request.History is null || request.Configuration.AgentModels is null
            || request.Configuration.PromptBlocks is null
            || request.History.Any(message => message is null))
            throw new DomainException("invalid_request", "Configurazione e cronologia non possono essere null.");
        var settings = request.Configuration;
        if (settings.UnboundedExecution && !(Configuration.GetValue<bool?>("Demo:AllowUnboundedExecution") ?? true))
            throw new DomainException("unbounded_disabled", "Esecuzione senza limiti non abilitata sul backend.");
        if (settings.UnboundedExecution && settings.ApprovedBudgetUsd is not null)
            throw new DomainException("conflicting_budget", "Senza limiti richiede ApprovedBudgetUsd=null.");
        if (!DemoTechnologies.All.Contains(request.Technology))
            throw new DomainException("unknown_technology", "Tecnologia non supportata.");
        if (settings.Mode != "live")
            throw new DomainException("unknown_mode", "È supportata solo l'esecuzione LIVE.");
        if (settings.PromptProfile is not ("bad" or "good" or "gpt5" or "gpt6"))
            throw new DomainException("unknown_prompt_profile", "Profilo prompt non registrato.");
        if (settings.HistoryStrategy is not ("full" or "compact"))
            throw new DomainException("unknown_history_strategy", "Strategia storia non supportata.");
        if (settings.ToolTransport != "direct")
            throw new DomainException("unsupported_transport", "MCP non implementato in questa release; selezionare direct.");
        if (settings.MaxModelCalls is < 1 or > 128 || settings.MaxOutputTokens is < 1 or > 32000)
            throw new DomainException("invalid_limits", "MaxModelCalls deve essere 1–128; MaxOutputTokens 1–32000.");
        if (string.IsNullOrWhiteSpace(request.RunId) || request.RunId.Length > 128
            || request.RunId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new DomainException("invalid_run_id", "RunId deve essere un identificatore ASCII (massimo 128 caratteri).");
        if (string.IsNullOrWhiteSpace(request.CustomerId) || request.CustomerId.Length > 128
            || request.CustomerId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            || string.IsNullOrWhiteSpace(request.Message))
            throw new DomainException("invalid_request", "Cliente e messaggio sono obbligatori.");
        var activeAgents = AgentNames.ForTechnology(request.Technology);
        foreach (var agent in settings.AgentModels.Keys)
            if (!activeAgents.Contains(agent))
                throw new DomainException("unknown_agent", $"Agente non attivo per {request.Technology}: {agent}.");
        foreach (var agent in activeAgents)
        {
            var registration = ForAgent(request, agent);
            ValidateLive(request, registration);
        }
    }

    private void ValidateLive(AgentRunRequest request, ModelRegistration registration)
    {
        if (!AllowLive)
            throw new DomainException("live_disabled", "LIVE disabilitato: Demo:AllowLive deve essere esplicitamente true.");
        if (!registration.Model.Configured)
            throw new DomainException("deployment_required", "LIVE richiede AzureOpenAI:Endpoint e Models:{id}:Deployment.");
        if (!request.Configuration.UnboundedExecution && request.Configuration.ApprovedBudgetUsd is not > 0)
            throw new DomainException("budget_required", "LIVE richiede un budget USD esplicitamente approvato.");
        if (!Uri.TryCreate(Configuration["AzureOpenAI:Endpoint"], UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new DomainException("invalid_endpoint", "L'endpoint Azure OpenAI deve usare HTTPS.");
        if (!registration.FunctionCallingVerified || !registration.MaxOutputTokensVerified)
            throw new DomainException("unverified_capabilities", $"Verificare le capacità FunctionCalling e MaxOutputTokens del deployment {registration.Model.Id}; il nome del modello non prova compatibilità.");
        var pricing = registration.Model.Pricing;
        if ((TokenCostCalculator.UsesContextTiers(pricing) || pricing.CacheWritePerMillion is not null) &&
            !TokenCostCalculator.HasCompleteRates(pricing))
            throw new DomainException("pricing_required", "LIVE richiede tutte le tariffe e la soglia per input, cache read/write e output in entrambi i contesti.");
        if (pricing.InputPerMillion is not >= 0 || pricing.OutputPerMillion is not >= 0 || pricing.Currency != "USD"
            || pricing.CachedInputPerMillion < 0 || pricing.CacheWriteSurchargePerMillion < 0)
            throw new DomainException("pricing_required", "LIVE richiede tariffe input/output USD configurate per verificare il budget fra chiamate.");
    }
}
