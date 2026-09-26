using Microsoft.Extensions.Logging.Abstractions;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

internal static partial class ApiSelfCheck
{
    private static async Task<int> CheckUnboundedExecution(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        if (File.Exists(path)) throw new InvalidOperationException("Self-check refuses to overwrite a database.");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Path"] = path, ["Demo:AllowLive"] = "true", ["Processing:RunTimeoutSeconds"] = "1",
            ["AzureOpenAI:Endpoint"] = "https://fixture.invalid",
            ["Models:gpt5:Deployment"] = "fixture-only", ["Models:gpt5:InputPerMillion"] = "1",
            ["Models:gpt5:CachedInputPerMillion"] = "1", ["Models:gpt5:OutputPerMillion"] = "1",
            ["Models:gpt5:SourceUrl"] = "https://fixture.invalid/pricing",
            ["Models:gpt5:VerifiedAt"] = "2026-09-25",
            ["Models:gpt5:Capabilities:FunctionCalling"] = "true",
            ["Models:gpt5:Capabilities:MaxOutputTokens"] = "true"
        }).Build();
        var settings = new ObservatorySettings(configuration);
        var selected = new RunConfiguration { UnboundedExecution = true, MaxModelCalls = 1, MaxOutputTokens = 1 };
        var agentRequest = new AgentRunRequest
        {
            RunId = "fixture", ConversationId = "fixture", Technology = "a2a", Message = "fixture", Configuration = selected
        };
        ThrowsApi(() => settings.ValidateConfiguration(selected), 403, "unbounded_disabled");
        Throws<DomainException>(() => new AgentModelRegistry(configuration).Validate(agentRequest), "Specialist requires server opt-in.");
        configuration["Demo:AllowUnboundedExecution"] = "true";
        settings.ValidateConfiguration(selected);
        new AgentModelRegistry(configuration).Validate(agentRequest);
        ThrowsApi(() => settings.ValidateConfiguration(selected with { UnboundedExecution = false }), 422, "budget_required");
        ThrowsApi(() => settings.ValidateConfiguration(selected with { ApprovedBudgetUsd = .1m }), 400, "conflicting_budget");
        Check(!ApiJson.Serialize(new RunConfiguration()).Contains("unboundedExecution"), "Existing bounded payloads retain their serialized form.");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var store = new EvidenceStore(settings);
        var coordinator = new RunCoordinator(store, settings, new FixtureShop());
        using var worker = new RunWorker(store, coordinator, new CompletedBatchFixture(), settings,
            new(configuration), NullLogger<RunWorker>.Instance);
        await worker.StartAsync(deadline.Token);
        try
        {
            foreach (var message in new[] { "unbounded-delay", "usage-drain", "bounded-limit" })
            {
                var request = new SubmitTurnRequest
                {
                    Message = message,
                    Configuration = message == "bounded-limit"
                        ? selected with { UnboundedExecution = false, ApprovedBudgetUsd = 1 } : selected
                };
                var id = coordinator.Submit(store.CreateConversation(message).Id, request, ApiJson.Serialize(request)).Run.Id;
                var run = await coordinator.WaitForCompletion(id, deadline.Token);
                Check(run.Calls.Count == 3, "All fixture calls remain in the ledger.");
                if (message == "unbounded-delay")
                    Check(run.Status == "completed" && run.EstimatedCostUsd == .003m && run.DurationMs >= 1500,
                        "Unbounded run exceeds configured one-call and one-second limits with no budget and keeps accounting.");
                else
                    Check(run.Status == "failed" && run.Error?.Contains(message == "usage-drain"
                        ? "pricing is incomplete" : "model-call limit") == true,
                        "Unknown usage still fails closed; bounded runs still enforce their call limit.");
            }
        }
        finally { await worker.StopAsync(deadline.Token); }
        Console.WriteLine("PASS: unbounded API/runtime opt-in, budget conflict, legacy serialization, timeout/call bypass, accounting, bounded limits and unknown-usage protection. No provider called.");
        return 0;
    }
}
