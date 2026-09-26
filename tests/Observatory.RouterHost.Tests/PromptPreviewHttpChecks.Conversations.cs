using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

internal static partial class PromptPreviewHttpChecks
{
    private const string HeldTurnMessage = "Primo messaggio: conserva queste impostazioni.";
    private const string OrdinalTurnMessage = "Quanto costa il primo?";
    private const string OverrideTurnMessage = "Terzo messaggio: conserva gli override espliciti.";

    private static async Task CheckConversationConfiguration(HttpClient client, string technology,
        IServiceProvider services, CancellationToken token)
    {
        var store = services.GetRequiredService<EvidenceStore>();
        var coordinator = services.GetRequiredService<RunCoordinator>();
        var original = new RunConfiguration
        {
            Mode = "live", ModelProfileId = "gpt5", PromptProfile = "bad",
            PromptBlocks = new() { Checklist = true }, ApprovedBudgetUsd = 1m
        };
        var changed = original with
        {
            ModelProfileId = "gpt6-luna", PromptProfile = "good",
            PromptBlocks = new() { OutputContract = true, Examples = true }
        };
        var unsent = changed with
        {
            ModelProfileId = "gpt6-astra", PromptProfile = "gpt6", PromptBlocks = new() { ConflictingStyle = true }
        };
        var before = ConversationState(store);
        await CheckPreview(client, technology, ApiJson.Serialize(changed), changed, token);
        Check(before == ConversationState(store),
            "Changing settings and previewing before a chat never creates a conversation or run.");
        var conversationsBefore = store.Conversations().Count;
        var runsBefore = store.Runs().Count;
        string conversationId;
        using (var content = new StringContent("""{"title":"Impostazioni per messaggio"}""", Encoding.UTF8, "application/json"))
        using (var response = await client.PostAsync("/api/conversations", content, token))
        {
            Check(response.StatusCode == HttpStatusCode.Created, "Create the configuration test conversation over real HTTP.");
            conversationId = (await ReadJson(response, token)).GetProperty("id").GetString()!;
        }

        var probe = new ConversationRuntimeProbe();
        using var worker = new RunWorker(store, coordinator, probe, services.GetRequiredService<ObservatorySettings>(),
            services.GetRequiredService<EvidenceSanitizer>(), NullLogger<RunWorker>.Instance);
        await worker.StartAsync(token);
        try
        {
            var first = new SubmitTurnRequest { Message = HeldTurnMessage, IdempotencyKey = "settings-first", Configuration = original };
            var firstId = await SubmitConfigurationTurn(client, conversationId, first, token);
            await probe.HeldTurnStarted.Task.WaitAsync(token);
            Check(store.GetRun(firstId).Status == "running" && probe.Requests.Count == 1,
                "Hold the actual worker's first run while settings are changed.");
            CheckFrozenConfiguration(store, firstId, first);
            CheckJsonEqual(JsonSerializer.SerializeToElement(probe.Requests[firstId].Configuration, ApiJson.Options),
                JsonSerializer.SerializeToElement(original, ApiJson.Options), "The running request receives GPT5/bad and its original blocks.");
            before = ConversationState(store);
            await CheckPreview(client, technology, ApiJson.Serialize(changed), changed, token);
            Check(before == ConversationState(store) && probe.Requests.Count == 1,
                "Previewing GPT6/good while a turn runs neither mutates its state nor invokes another run.");

            var second = new SubmitTurnRequest
            {
                Message = OrdinalTurnMessage, IdempotencyKey = "settings-second", Configuration = changed
            };
            var secondId = await SubmitConfigurationTurn(client, conversationId, second, token);
            Check(store.GetRun(secondId).Status == "queued" && !probe.Requests.ContainsKey(secondId),
                "The next message in the same conversation waits for its predecessor.");
            CheckFrozenConfiguration(store, secondId, second);
            before = ConversationState(store);
            await CheckPreview(client, technology, ApiJson.Serialize(unsent), unsent, token);
            Check(before == ConversationState(store) && probe.Requests.Count == 1,
                "Another unsent settings change cannot alter either the active run or the queued run.");
            CheckFrozenConfiguration(store, firstId, first);
            CheckFrozenConfiguration(store, secondId, second);

            var overrides = new Dictionary<string, string> { ["router"] = "gpt5" };
            if (technology == DemoTechnologies.A2A) overrides["catalog"] = "gpt6-astra";
            var overridden = changed with { AgentModels = overrides };
            var third = new SubmitTurnRequest
            {
                Message = OverrideTurnMessage, IdempotencyKey = "settings-third", Configuration = overridden
            };
            var thirdId = await SubmitConfigurationTurn(client, conversationId, third, token);
            Check(store.GetRun(thirdId).Status == "queued" && !probe.Requests.ContainsKey(thirdId),
                "A future turn is already persisted before the ordinal-detail turn's history is captured.");
            CheckFrozenConfiguration(store, thirdId, third);

            probe.ContinueHeldTurn.TrySetResult();
            await probe.OrdinalTurnStarted.Task.WaitAsync(token);
            var firstRun = await coordinator.WaitForCompletion(firstId, token);
            Check(firstRun.Status == "completed" && store.GetRun(secondId).Status == "running"
                  && store.GetRun(thirdId).Status == "queued",
                "Hold the ordinal question with a completed catalog predecessor and a queued future turn.");
            Check(probe.Requests[firstId].History.Count == 0
                  && probe.Requests[secondId].History.Select(message => (message.Role, message.Text))
                      .SequenceEqual(new[] { ("user", first.Message), ("assistant", firstRun.Result!.Answer) }),
                "Changing model and prompt retains the first turn's real history without adding preview text.");
            Check(firstRun.Result!.ProductIds.SequenceEqual([83, 86])
                  && firstRun.Result!.Sources.SequenceEqual(["catalog:DummyJSON"]),
                "The first completed HTTP turn stores ordered catalog product IDs and verified provenance.");
            var catalogHistory = probe.Requests[secondId].History.Single(message => message.Role == "assistant");
            Check(catalogHistory.ProductIds.SequenceEqual([83, 86])
                  && catalogHistory.Sources.SequenceEqual(["catalog:DummyJSON"]),
                "The next runtime can resolve 'il primo' from the same ordered, server-stored catalog references.");
            Check(probe.Requests[secondId].History.All(message => message.RunId == firstId)
                  && !probe.Requests.ContainsKey(thirdId),
                "Runtime history excludes the current question and the already-persisted future turn.");
            CheckJsonEqual(JsonSerializer.SerializeToElement(probe.Requests[secondId].History, ApiJson.Options),
                JsonSerializer.SerializeToElement(store.GetConversation(conversationId).Messages.Take(2), ApiJson.Options),
                "API history preserves preceding message text, order, identity, product IDs and sources without lossy copies.");
            CheckJsonEqual(JsonSerializer.SerializeToElement(probe.Requests[secondId].Configuration, ApiJson.Options),
                JsonSerializer.SerializeToElement(changed, ApiJson.Options), "The next runtime request receives GPT6/good and the changed blocks.");
            CheckFixtureModels(firstRun, AgentNames.ForTechnology(technology).ToDictionary(agent => agent, _ => "gpt5"));
            var savedFirst = ApiJson.Serialize(firstRun);
            var capturedFirst = ApiJson.Serialize(probe.Requests[firstId]);
            probe.ContinueOrdinalTurn.TrySetResult();
            await probe.OverrideTurnStarted.Task.WaitAsync(token);
            var secondRun = await coordinator.WaitForCompletion(secondId, token);
            Check(secondRun.Status == "completed", "The ordinal-detail turn completes with its own frozen settings.");
            CheckFixtureModels(secondRun, AgentNames.ForTechnology(technology).ToDictionary(agent => agent, _ => "gpt6-luna"));
            var savedSecond = ApiJson.Serialize(secondRun);
            var retainedMessages = ApiJson.Serialize(store.GetConversation(conversationId).Messages.Take(4));
            var capturedSecond = ApiJson.Serialize(probe.Requests[secondId]);

            CheckJsonEqual(JsonSerializer.SerializeToElement(probe.Requests[thirdId].History, ApiJson.Options),
                JsonSerializer.SerializeToElement(store.GetConversation(conversationId).Messages.Take(4), ApiJson.Options),
                "Catalog references survive intervening turns; the third turn excludes its own message.");
            probe.ContinueOverrideTurn.TrySetResult();
            var thirdRun = await coordinator.WaitForCompletion(thirdId, token);
            Check(thirdRun.Status == "completed", "An explicit per-agent override remains valid after changing the default model.");
            CheckFrozenConfiguration(store, thirdId, third);
            CheckJsonEqual(JsonSerializer.SerializeToElement(probe.Requests[thirdId].Configuration, ApiJson.Options),
                JsonSerializer.SerializeToElement(overridden, ApiJson.Options), "Default model and explicit overrides survive independently in the runtime request.");
            var expectedModels = AgentNames.ForTechnology(technology).ToDictionary(agent => agent, _ => "gpt6-luna");
            expectedModels["router"] = "gpt5";
            if (technology == DemoTechnologies.A2A) expectedModels["catalog"] = "gpt6-astra";
            CheckFixtureModels(thirdRun, expectedModels);
            Check(probe.Requests[thirdId].History.Select(message => (message.Role, message.Text))
                    .SequenceEqual(new[]
                    {
                        ("user", first.Message), ("assistant", firstRun.Result!.Answer),
                        ("user", second.Message), ("assistant", secondRun.Result!.Answer)
                    }),
                "The override turn receives all earlier conversation text, regardless of earlier model/profile choices.");
            Check(probe.Requests.Values.All(request => request.ConversationId == conversationId)
                  && store.GetConversation(conversationId).Messages.Count == 6,
                "All three configurations belong to the same conversation with exactly three user/assistant pairs.");
            Check(retainedMessages == ApiJson.Serialize(store.GetConversation(conversationId).Messages.Take(4))
                  && capturedFirst == ApiJson.Serialize(probe.Requests[firstId]) && capturedSecond == ApiJson.Serialize(probe.Requests[secondId])
                  && savedFirst == ApiJson.Serialize(store.GetRun(firstId)) && savedSecond == ApiJson.Serialize(store.GetRun(secondId)),
                "Later configurations never rewrite completed runs, captured history or earlier persisted messages.");
            CheckFrozenConfiguration(store, firstId, first);
            CheckFrozenConfiguration(store, secondId, second);

            before = ConversationState(store);
            var invalid = third with
            {
                IdempotencyKey = "settings-invalid",
                Configuration = changed with
                {
                    AgentModels = new() { [technology == DemoTechnologies.A2A ? "inactive" : "catalog"] = "gpt5" }
                }
            };
            await CheckError(client, $"/api/conversations/{conversationId}/turns", ApiJson.Serialize(invalid),
                400, "invalid_agent_name", token);
            await CheckPreview(client, technology, ApiJson.Serialize(unsent), unsent, token);
            Check(before == ConversationState(store) && probe.Requests.Count == 3
                  && store.Conversations().Count == conversationsBefore + 1 && store.Runs().Count == runsBefore + 3,
                "Invalid overrides and unsent previews create no messages, conversations, runs or runtime executions.");
            Console.WriteLine($"PASS {technology}: next-turn settings, frozen blocks/overrides, retained catalog references, current/future history exclusion and stateless preview (fake runtime only).");
        }
        finally
        {
            probe.ContinueHeldTurn.TrySetResult();
            probe.ContinueOrdinalTurn.TrySetResult();
            probe.ContinueOverrideTurn.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static string ConversationState(EvidenceStore store) =>
        ApiJson.Serialize(new { conversations = store.Conversations(), runs = store.Runs() });

    private static async Task<string> SubmitConfigurationTurn(HttpClient client, string conversationId,
        SubmitTurnRequest request, CancellationToken token)
    {
        using var content = new StringContent(ApiJson.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/api/conversations/{conversationId}/turns", content, token);
        Check(response.StatusCode == HttpStatusCode.Accepted && response.Headers.GetValues("Idempotency-Replayed").Single() == "false",
            "A new turn is accepted with its own immutable submitted configuration.");
        var accepted = await ReadJson(response, token);
        Check(accepted.GetProperty("conversationId").GetString() == conversationId, "Turn submission preserves conversation identity.");
        return accepted.GetProperty("runId").GetString()!;
    }

    private static void CheckFrozenConfiguration(EvidenceStore store, string id, SubmitTurnRequest expected)
    {
        var snapshot = store.GetSnapshot(id);
        var configuration = JsonSerializer.SerializeToElement(expected.Configuration, ApiJson.Options);
        CheckJsonEqual(JsonSerializer.SerializeToElement(store.GetRun(id).Configuration, ApiJson.Options), configuration,
            "The stored run freezes model, prompt profile, blocks and override settings at submission.");
        CheckJsonEqual(JsonSerializer.SerializeToElement(snapshot.Request.Configuration, ApiJson.Options), configuration,
            "Snapshot configuration remains exactly the submitted per-turn selection.");
        Check(snapshot.ExactRequestBody == ApiJson.Serialize(expected) && snapshot.Request.Message == expected.Message
              && snapshot.Request.IdempotencyKey == expected.IdempotencyKey,
            "Settings changes cannot replace the original request body, message or idempotency identity.");
    }

    private static void CheckFixtureModels(RunRecord run, Dictionary<string, string> expected)
    {
        Check(run.Calls.Count == expected.Count && run.Calls.All(call => expected.GetValueOrDefault(call.Agent) == call.ModelProfileId),
            "Persisted fixture calls retain explicit agent overrides and the default for non-overridden active agents.");
        Check(run.Calls.All(call => call.Mode == "live" && call.UsageSource == "fixture" && call.InputTokens is null
              && call.OutputTokens is null && call.EstimatedCostUsd is null),
            "The offline fixture does not claim provider usage or model-quality measurements.");
    }

    private sealed class ConversationRuntimeProbe : IAgentRuntime
    {
        public ConcurrentDictionary<string, AgentRunRequest> Requests { get; } = new();
        public TaskCompletionSource HeldTurnStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueHeldTurn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OrdinalTurnStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueOrdinalTurn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OverrideTurnStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueOverrideTurn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
            CancellationToken cancellationToken = default)
        {
            if (request.Configuration.Mode != "live") throw new InvalidOperationException("Configuration fixture accepts only LIVE-shaped requests.");
            if (!Requests.TryAdd(request.RunId, request)) throw new InvalidOperationException("A submitted turn executed twice.");
            if (request.Message == HeldTurnMessage)
            {
                HeldTurnStarted.TrySetResult();
                await ContinueHeldTurn.Task.WaitAsync(cancellationToken);
            }
            else if (request.Message == OrdinalTurnMessage)
            {
                OrdinalTurnStarted.TrySetResult();
                await ContinueOrdinalTurn.Task.WaitAsync(cancellationToken);
            }
            else if (request.Message == OverrideTurnMessage)
            {
                OverrideTurnStarted.TrySetResult();
                await ContinueOverrideTurn.Task.WaitAsync(cancellationToken);
            }
            var answer = request.Message == HeldTurnMessage
                ? "Catalogo di test: prodotto 83, poi prodotto 86."
                : "Risposta del runtime di test; nessuna inferenza eseguita.";
            foreach (var agent in AgentNames.ForTechnology(request.Technology))
                await emit(new()
                {
                    RunId = request.RunId, Kind = "model.completed", Agent = agent,
                    Data = new ModelCallRecord
                    {
                        RunId = request.RunId, Agent = agent, Mode = "live", UsageSource = "fixture", CaptureKind = "logical",
                        ModelProfileId = request.Configuration.AgentModels.GetValueOrDefault(agent, request.Configuration.ModelProfileId),
                        Request = new { provider = "configuration-test-fixture", request.Message, request.Configuration, request.History },
                        Response = new { text = answer }
                    }
                });
            return request.Message == HeldTurnMessage
                ? new(answer, [83, 86], ["catalog:DummyJSON"])
                : new(answer, [], ["configuration-test-fixture"]);
        }
    }
}
