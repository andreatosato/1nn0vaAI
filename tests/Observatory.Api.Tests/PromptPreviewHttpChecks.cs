using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

internal static partial class PromptPreviewHttpChecks
{
    private static readonly string[] BlockIds = ["checklist", "outputContract", "examples", "redundancy", "conflictingStyle"];
    private static int checks;

    public static async Task<int> Run(string databasePath)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        foreach (var technology in DemoTechnologies.All)
        {
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                $"prompt-preview-{technology}-{Guid.NewGuid():N}.sqlite3");
            Check(!File.Exists(path), "HTTP self-check never overwrites an existing database.");
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
                builder.Configuration.Sources.Clear();
                var fixtureSettings = new Dictionary<string, string?>
                {
                    ["Demo:Technology"] = technology, ["Demo:AllowLive"] = "true",
                    ["AzureOpenAI:Endpoint"] = "https://fixture.openai.azure.com",
                    ["Storage:Path"] = path
                };
                foreach (var modelId in new[] { "gpt5", "gpt6-astra", "gpt6-sol", "gpt6-luna" })
                {
                    var prefix = $"Models:{modelId}:";
                    fixtureSettings[prefix + "Deployment"] = $"fixture-{modelId}";
                    fixtureSettings[prefix + "InputPerMillion"] = "1";
                    fixtureSettings[prefix + "CachedInputPerMillion"] = "0.1";
                    fixtureSettings[prefix + "OutputPerMillion"] = "2";
                    fixtureSettings[prefix + "SourceUrl"] = "https://example.com/fixture-pricing";
                    fixtureSettings[prefix + "VerifiedAt"] = "2026-09-25";
                    fixtureSettings[prefix + "Capabilities:FunctionCalling"] = "true";
                    fixtureSettings[prefix + "Capabilities:MaxOutputTokens"] = "true";
                }
                builder.Configuration.AddInMemoryCollection(fixtureSettings);
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Logging.ClearProviders();
                builder.Services.AddProblemDetails();
                builder.Services.AddSingleton<ObservatorySettings>();
                builder.Services.AddSingleton<EvidenceSanitizer>();
                var catalog = new CatalogProbe();
                var runtime = new RuntimeProbe();
                builder.Services.AddSingleton<IShopCatalog>(catalog);
                builder.Services.AddSingleton<IAgentRuntime>(runtime);
                var allowPersistence = false;
                var storeCreations = 0;
                builder.Services.AddSingleton(services =>
                {
                    Interlocked.Increment(ref storeCreations);
                    if (!allowPersistence) throw new InvalidOperationException("Preview unexpectedly requested state.");
                    return new EvidenceStore(services.GetRequiredService<ObservatorySettings>());
                });
                builder.Services.AddSingleton<RunCoordinator>();
                builder.Services.AddSingleton<ExperimentService>();
                await using var app = builder.Build();
                app.UseObservatoryErrors();
                app.MapObservatory();
                await app.StartAsync(deadline.Token);
                try
                {
                    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                    using var client = new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(10) };
                    using (var response = await client.GetAsync("/api/config", deadline.Token))
                    {
                        Check(response.StatusCode == HttpStatusCode.OK, "Real Kestrel API fixture is responsive.");
                        var config = await ReadJson(response, deadline.Token);
                        Check(config.GetProperty("technology").GetString() == technology
                              && config.GetProperty("allowLive").GetBoolean(), "Fixture models are LIVE-ready without using provider inference.");
                        var definitions = config.GetProperty("promptBlocks");
                        Check(definitions.EnumerateArray().Select(block => block.GetProperty("id").GetString()).SequenceEqual(BlockIds),
                            "Config publishes exactly the five selectable block IDs, not the legacy profile list.");
                        foreach (var definition in definitions.EnumerateArray())
                        {
                            CheckProperties(definition, ["id", "label", "description"]);
                            Check(!string.IsNullOrWhiteSpace(definition.GetProperty("label").GetString())
                                  && !string.IsNullOrWhiteSpace(definition.GetProperty("description").GetString()),
                                "Config block labels and descriptions are nonempty.");
                        }
                        CheckJsonEqual(definitions, JsonSerializer.SerializeToElement(PromptLaboratory.Blocks, ApiJson.Options),
                            "HTTP configuration exposes the authoritative block catalog without renaming fields.");
                    }
                    var catalogReads = catalog.Reads;
                    await CheckPreview(client, technology, "{}", new(), deadline.Token);
                    await CheckPreview(client, technology, """{"promptProfile":"bad","promptBlocks":{}}""",
                        new() { PromptProfile = "bad" }, deadline.Token);
                    await CheckPreview(client, technology, """{"promptBlocks":{"examples":true}}""",
                        new() { PromptBlocks = new() { Examples = true } }, deadline.Token);
                    for (var mask = 0; mask < 32; mask++)
                    {
                        var configuration = new RunConfiguration { PromptBlocks = Selection(mask), ConfirmAction = (mask & 1) != 0 };
                        await CheckPreview(client, technology, ApiJson.Serialize(configuration), configuration, deadline.Token);
                    }
                    foreach (var profile in new[] { "bad", "good", "gpt5", "gpt6" })
                    {
                        var live = new RunConfiguration
                        {
                            Mode = "live", PromptProfile = profile, PromptBlocks = Selection(31),
                            HistoryStrategy = "compact", AgentModels = new() { ["router"] = "gpt6-luna" },
                            MaxModelCalls = 64, MaxOutputTokens = 16384
                        };
                        await CheckPreview(client, technology, ApiJson.Serialize(live), live, deadline.Token);
                    }
                    if (technology == DemoTechnologies.A2A)
                    {
                        var overrides = new RunConfiguration { AgentModels = new() { ["catalog"] = "gpt6-astra" } };
                        await CheckPreview(client, technology, ApiJson.Serialize(overrides), overrides, deadline.Token);
                    }
                    else
                        await CheckError(client, "/api/prompts/preview", """{"agentModels":{"catalog":"gpt5"}}""",
                            400, "invalid_agent_name", deadline.Token);
                    await CheckInvalidPreviews(client, deadline.Token);
                    Check(storeCreations == 0 && !File.Exists(path) && !File.Exists(path + ".lock"),
                        "Valid, LIVE and rejected previews never resolve EvidenceStore or create SQLite state.");
                    Check(runtime.Calls == 0 && catalog.Reads == catalogReads,
                        "Preview reads neither the runtime nor catalog/domain facts; no models or tools execute.");

                    allowPersistence = true;
                    var store = app.Services.GetRequiredService<EvidenceStore>();
                    var settings = app.Services.GetRequiredService<ObservatorySettings>();
                    await CheckStoredSettings(client, technology, store, settings, catalog, runtime, deadline.Token);
                    await CheckConversationConfiguration(client, technology, app.Services, deadline.Token);
                }
                finally
                {
                    await app.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                foreach (var suffix in new[] { "", "-wal", "-shm", ".lock" })
                    if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
        }
        Console.WriteLine($"PASS: {checks} real-HTTP prompt assertions: catalog, 32 selections, preview isolation, per-turn configuration/history, overrides, validation, replay/export and unchanged run gating.");
        return 0;
    }

    private static async Task CheckPreview(HttpClient client, string technology, string body,
        RunConfiguration configuration, CancellationToken token)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/prompts/preview", content, token);
        Check(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "application/json",
            $"Preview accepts full/legacy RunConfiguration as JSON: {technology}/{configuration.PromptProfile}.");
        var preview = await ReadJson(response, token);
        CheckProperties(preview, ["technology", "agents", "notice"]);
        CheckJsonEqual(preview, JsonSerializer.SerializeToElement(PromptLaboratory.Preview(technology, configuration), ApiJson.Options),
            "HTTP preview returns the exact public instructions and notice, not a derived/synthetic prompt.");
        Check(!string.IsNullOrWhiteSpace(preview.GetProperty("notice").GetString()), "Preview includes its limitations notice.");
        string[] expected = technology == DemoTechnologies.A2A ? ["router", "catalog", "orders", "returns"] : ["router"];
        Check(preview.GetProperty("agents").EnumerateArray().Select(agent => agent.GetProperty("agent").GetString()).SequenceEqual(expected),
            "HTTP preview exposes only this architecture's active model agents.");
        foreach (var agent in preview.GetProperty("agents").EnumerateArray())
        {
            CheckProperties(agent, ["agent", "instructions", "characterCount"]);
            Check(agent.GetProperty("characterCount").GetInt32() == agent.GetProperty("instructions").GetString()!.Length,
                "Wire character counts match actual UTF-16 instruction strings.");
        }
    }

    private static async Task CheckInvalidPreviews(HttpClient client, CancellationToken token)
    {
        var invalid = new (string Body, string Code)[]
        {
            ("", "invalid_json"),
            ("{", "invalid_json"),
            ("null", "invalid_json"),
            ("[]", "invalid_json"),
            ("""{"configuration":{}}""", "invalid_json"),
            ("""{"unknown":true}""", "invalid_json"),
            ("""{"promptBlocks":null}""", "invalid_prompt_blocks"),
            ("""{"promptBlocks":[]}""", "invalid_json"),
            ("""{"promptBlocks":true}""", "invalid_json"),
            ("""{"promptBlocks":{"checklist":"true"}}""", "invalid_json"),
            ("""{"promptBlocks":{"examples":1}}""", "invalid_json"),
            ("""{"promptBlocks":{"redundancy":null}}""", "invalid_json"),
            ("""{"promptBlocks":{"invented":true}}""", "invalid_json"),
            ("""{"mode":"offline","Mode":"live"}""", "duplicate_property"),
            ("""{"promptBlocks":{"checklist":true,"Checklist":false}}""", "duplicate_property"),
            ("""{"mode":"invented"}""", "invalid_mode"),
            ("""{"mode":null}""", "invalid_mode"),
            ("""{"promptProfile":"invented"}""", "invalid_prompt_profile"),
            ("""{"promptProfile":null}""", "invalid_prompt_profile"),
            ("""{"historyStrategy":"invented"}""", "invalid_history_strategy"),
            ("""{"toolTransport":"mcp"}""", "invalid_tool_transport"),
            ("""{"maxModelCalls":0}""", "invalid_call_limit"),
            ("""{"maxModelCalls":65}""", "invalid_call_limit"),
            ("""{"maxOutputTokens":0}""", "invalid_output_limit"),
            ("""{"maxOutputTokens":16385}""", "invalid_output_limit"),
            ("""{"agentModels":null}""", "invalid_agent_models"),
            ("""{"agentModels":{"unknown":"gpt5"}}""", "invalid_agent_name"),
            ("""{"agentModels":{"router":"unknown"}}""", "invalid_model_profile"),
            ("""{"modelProfileId":"unknown"}""", "invalid_model_profile"),
            ("""{"approvedBudgetUsd":0}""", "invalid_budget"),
            ("""{"approvedBudgetUsd":11}""", "invalid_budget")
        };
        foreach (var item in invalid)
            await CheckError(client, "/api/prompts/preview", item.Body, 400, item.Code, token);
        // Accepts metadata rejects this before ReadBody; the existing status-code handler supplies the problem.
        await CheckError(client, "/api/prompts/preview", "{}", 415, null, token, "text/plain");
        await CheckError(client, "/api/prompts/preview", new string(' ', 65537), 413, "body_too_large", token);
    }

    private static async Task CheckStoredSettings(HttpClient client, string technology, EvidenceStore store,
        ObservatorySettings settings, IShopCatalog catalog, RuntimeProbe runtime, CancellationToken token)
    {
        var conversation = store.CreateConversation("Prompt settings HTTP replay");
        foreach (var mask in new[] { 0, 21, 31 })
        {
            var request = new SubmitTurnRequest
            {
                Message = "Stored prompt settings; do not execute.", IdempotencyKey = $"prompt-blocks-{mask}",
                Configuration = new() { PromptProfile = "bad", PromptBlocks = Selection(mask) }
            };
            var exact = JsonSerializer.Serialize(request, new JsonSerializerOptions(ApiJson.Options) { WriteIndented = true });
            var run = store.Submit(conversation.Id, settings.Snapshot(request, exact, catalog)).Run;
            store.Cancel(run.Id);
            var expected = JsonSerializer.SerializeToElement(request.Configuration.PromptBlocks, ApiJson.Options);
            using (var response = await client.PostAsync($"/api/runs/{run.Id}/replay", null, token))
            {
                Check(response.StatusCode == HttpStatusCode.OK, "Stored prompt configuration can be replayed through real HTTP.");
                Check(response.Headers.GetValues("X-Original-Run-Id").Single() == run.Id
                      && response.Headers.GetValues("X-Replay-Only").Single() == "true",
                    "Replay identifies the original run and does not claim fresh inference.");
                var replay = await ReadJson(response, token);
                Check(replay.GetProperty("replayOnly").GetBoolean() && replay.GetProperty("originalRunId").GetString() == run.Id,
                    "Replay body retains its read-only provenance.");
                CheckJsonEqual(replay.GetProperty("configuration").GetProperty("promptBlocks"), expected,
                    "Replay retains every selected and unselected block.");
            }
            using (var response = await client.GetAsync($"/api/runs/{run.Id}/export", token))
            {
                Check(response.StatusCode == HttpStatusCode.OK, "Stored prompt settings export successfully.");
                var export = await ReadJson(response, token);
                CheckJsonEqual(export.GetProperty("run").GetProperty("configuration").GetProperty("promptBlocks"), expected,
                    "Exported run retains optional prompt selections.");
                CheckJsonEqual(export.GetProperty("provenance").GetProperty("request").GetProperty("configuration").GetProperty("promptBlocks"), expected,
                    "Export provenance retains the frozen request's optional prompt selections.");
                Check(export.GetProperty("provenance").GetProperty("exactRequestBody").GetString() == exact,
                    "Export preserves the exact submitted prompt-settings body, including whitespace.");
            }
        }
        var before = ApiJson.Serialize(new { conversations = store.Conversations(), runs = store.Runs() });
        var previews = new RunConfiguration { Mode = "live", PromptBlocks = Selection(31) };
        await CheckPreview(client, technology, ApiJson.Serialize(previews), previews, token);
        await CheckError(client, $"/api/conversations/{conversation.Id}/turns",
            ApiJson.Serialize(new SubmitTurnRequest { Message = "This is not authorized.", Configuration = previews }),
            403, "live_disabled", token);
        Check(before == ApiJson.Serialize(new { conversations = store.Conversations(), runs = store.Runs() }),
            "Previewing LIVE instructions never authorizes a run or changes persisted conversations/runs.");
        Check(store.Runs().Count == 3 && store.GetConversation(conversation.Id).Messages.Count == 3
              && store.Runs().All(run => run.Status == "cancelled" && run.Calls.Count == 0) && runtime.Calls == 0,
            "Preview, replay and export never create an extra run, model call or tool operation.");
    }

    private static async Task CheckError(HttpClient client, string path, string body, int status, string? code,
        CancellationToken token, string mediaType = "application/json")
    {
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        using var response = await client.PostAsync(path, content, token);
        Check((int)response.StatusCode == status && response.Content.Headers.ContentType?.MediaType == "application/problem+json",
            $"Expected HTTP {status} Problem Details for {code}; got {(int)response.StatusCode}.");
        var problem = await ReadJson(response, token);
        Check(problem.TryGetProperty("status", out var actualStatus) && actualStatus.GetInt32() == status
              && problem.TryGetProperty("title", out var title) && title.GetString() == (code ?? "http_error")
              && problem.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString()),
            $"HTTP error retains status, title and detail for {code ?? "http_error"}: {problem.GetRawText()}.");
        if (code is not null)
            Check(problem.TryGetProperty("code", out var actualCode) && actualCode.GetString() == code
                  && problem.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()),
                $"Handled errors retain exact code and trace ID for {code}: {problem.GetRawText()}.");
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, CancellationToken token)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.Clone();
    }

    private static PromptBlockSelection Selection(int mask) => new()
    {
        Checklist = (mask & 1) != 0, OutputContract = (mask & 2) != 0, Examples = (mask & 4) != 0,
        Redundancy = (mask & 8) != 0, ConflictingStyle = (mask & 16) != 0
    };

    private static void CheckProperties(JsonElement value, string[] expected) =>
        Check(value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "JSON uses the exact camelCase response property contract.");

    private static void CheckJsonEqual(JsonElement actual, JsonElement expected, string message) =>
        Check(JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected.GetRawText())), message);

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("HTTP PROMPT SELF-CHECK FAILED: " + message);
    }

    private sealed class RuntimeProbe : IAgentRuntime
    {
        public int Calls;
        public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("Prompt preview or replay unexpectedly invoked the runtime.");
        }
    }

    private sealed class CatalogProbe : IShopCatalog
    {
        private readonly CatalogSnapshot catalog = new() { Source = "Prompt preview fixture", SourceUrl = "https://example.invalid/catalog", Products = [] };
        public int Reads;
        public CatalogSnapshot Catalog { get { Interlocked.Increment(ref Reads); return catalog; } }
        public IReadOnlyList<Product> Products { get { Interlocked.Increment(ref Reads); return catalog.Products; } }
        public IReadOnlyList<ScenarioDefinition> Scenarios { get { Interlocked.Increment(ref Reads); return []; } }
    }
}
