using Observatory.Core;

namespace Observatory.RouterHost;

public sealed class ExperimentService(IShopCatalog data, ObservatorySettings settings)
{
    public object Estimate(ExperimentRequest request)
    {
        var scenarios = Validate(request);
        var turnCount = scenarios.Sum(s => s.Turns.Count);
        long? callCeiling = request.Configurations.Any(c => c.UnboundedExecution) ? null
            : request.Configurations.Sum(c => (long)c.MaxModelCalls) * turnCount * request.Repetitions;
        return new
        {
            dryRun = true, scenarioCount = scenarios.Count, turnCount,
            configurationCount = request.Configurations.Length, request.Repetitions,
            estimatedModelCalls = callCeiling, estimatedCostUsd = (decimal?)null,
            notice = "No run created and no provider called. estimatedModelCalls is the configured upper bound, not measured usage or a price quote."
        };
    }

    public async Task<ExperimentResult> Execute(ExperimentRequest request, CancellationToken cancellationToken)
    {
        _ = Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new ApiException(403, "paid_batch_disabled",
            "L'esecuzione batch non è supportata: usa la chat per autorizzare ogni singolo invio LIVE.");
    }

    private IReadOnlyList<ScenarioDefinition> Validate(ExperimentRequest request)
    {
        if (request.ScenarioIds is null || request.ScenarioIds.Length is < 1 or > 32 ||
            request.ScenarioIds.Any(string.IsNullOrWhiteSpace) ||
            request.ScenarioIds.Distinct(StringComparer.Ordinal).Count() != request.ScenarioIds.Length)
            throw new ApiException(400, "invalid_scenarios", "Choose 1–32 distinct known scenario IDs.");
        if (request.Configurations is null || request.Configurations.Length is < 1 or > 16 ||
            request.Configurations.Any(c => c is null))
            throw new ApiException(400, "invalid_configurations", "Provide 1–16 configurations.");
        if (request.Repetitions is < 1 or > 10)
            throw new ApiException(400, "invalid_repetitions", "Repetitions must be between 1 and 10.");
        var scenarios = new List<ScenarioDefinition>();
        foreach (var id in request.ScenarioIds)
        {
            var scenario = data.Scenarios.SingleOrDefault(s => s.Id == id);
            if (scenario is null) throw new ApiException(400, "unknown_scenario", $"Unknown scenario '{id}'.");
            scenarios.Add(scenario);
        }
        foreach (var configuration in request.Configurations)
        {
            // A dry run may inspect LIVE choices without authorization because it never creates work.
            if (request.DryRun) settings.ValidatePromptPreview(configuration);
            else settings.ValidateConfiguration(configuration);
        }
        var turns = scenarios.Sum(s => s.Turns.Count) * request.Configurations.Length * request.Repetitions;
        if (turns > 512)
            throw new ApiException(400, "experiment_too_large", "An experiment may contain at most 512 turns.");
        return scenarios;
    }
}
