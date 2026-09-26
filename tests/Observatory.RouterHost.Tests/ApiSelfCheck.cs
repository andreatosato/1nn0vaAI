using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

internal static partial class ApiSelfCheck
{
    private static void CheckDefaultMode(IShopCatalog data)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:AllowLive"] = "true",
            ["AzureOpenAI:Endpoint"] = "https://fixture.openai.azure.com",
            ["Models:gpt5:Deployment"] = "fixture-only",
            ["Models:gpt5:InputPerMillion"] = "1",
            ["Models:gpt5:CachedInputPerMillion"] = "0.1",
            ["Models:gpt5:OutputPerMillion"] = "2",
            ["Models:gpt5:SourceUrl"] = "https://example.com/fixture-pricing",
            ["Models:gpt5:VerifiedAt"] = "2026-09-25",
            ["Models:gpt5:Capabilities:FunctionCalling"] = "true",
            ["Models:gpt5:Capabilities:MaxOutputTokens"] = "true"
        }).Build();
        var settings = new ObservatorySettings(configuration, TestArchitectures.Inline);
        Check(settings.AllowLive && settings.Describe(data).DefaultMode == "live",
            "A ready deployment is advertised as LIVE.");
        configuration["Demo:DefaultMode"] = "live";
        Check(new ObservatorySettings(configuration, TestArchitectures.Inline).Describe(data).DefaultMode == "live",
            "LIVE remains the only advertised execution mode.");
        configuration["Demo:AllowLive"] = "false";
        Check(new ObservatorySettings(configuration, TestArchitectures.Inline).Describe(data).DefaultMode == "live"
              && !new ObservatorySettings(configuration, TestArchitectures.Inline).AllowLive,
            "LIVE can be the only mode while its readiness gate remains disabled.");
        configuration["Demo:AllowLive"] = "true";
        configuration["Models:gpt5:CachedInputPerMillion"] = "";
        Check(new ObservatorySettings(configuration, TestArchitectures.Inline).Describe(data).DefaultMode == "live"
              && !new ObservatorySettings(configuration, TestArchitectures.Inline).AllowLive,
            "Missing verified pricing disables LIVE readiness without introducing another mode.");
        configuration["Models:gpt5:CachedInputPerMillion"] = "0.1";
        configuration["Models:gpt5:Pricing:CacheWritePerMillion"] = "1.25";
        configuration["Models:gpt5:Pricing:LongContextThresholdTokens"] = "272000";
        configuration["Models:gpt5:Pricing:LongContextInputPerMillion"] = "2";
        configuration["Models:gpt5:Pricing:LongContextCachedInputPerMillion"] = "0.2";
        configuration["Models:gpt5:Pricing:LongContextCacheWritePerMillion"] = "2.5";
        configuration["Models:gpt5:Pricing:LongContextOutputPerMillion"] = "3";
        var tieredSettings = new ObservatorySettings(configuration, TestArchitectures.Inline);
        Check(tieredSettings.AllowLive && tieredSettings.DefaultMode == "live",
            "Complete explicit context/write rate card permits the LIVE default.");
        Check(tieredSettings.Models.Single(m => m.Id == "gpt5").Pricing ==
            new AgentModelRegistry(configuration).Registrations.Single(m => m.Model.Id == "gpt5").Model.Pricing,
            "API and runtime parse the identical complete nested context/write rate card.");
        configuration["Models:gpt5:Pricing:LongContextOutputPerMillion"] = "";
        Check(new ObservatorySettings(configuration, TestArchitectures.Inline).DefaultMode == "live"
              && !new ObservatorySettings(configuration, TestArchitectures.Inline).AllowLive,
            "An incomplete long-context rate card disables readiness even for a short request.");
        configuration["Models:gpt5:Pricing:LongContextThresholdTokens"] = "invalid";
        Throws<InvalidOperationException>(() => new ObservatorySettings(configuration, TestArchitectures.Inline), "Invalid context threshold fails startup.");
        Throws<DomainException>(() => _ = new AgentModelRegistry(configuration).Registrations, "Runtime rejects invalid context threshold.");
        configuration["Models:gpt5:Pricing:LongContextThresholdTokens"] = "272000";
        configuration["Demo:DefaultMode"] = "invalid";
        Throws<InvalidOperationException>(() => new ObservatorySettings(configuration, TestArchitectures.Inline), "Invalid default mode fails startup.");
    }

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--unbounded")
            return await CheckUnboundedExecution(args[1]);
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project tests\\Observatory.Api.Tests -- <new-database-path>. Supply an explicit project-local path.");
            return 1;
        }
        var path = Path.GetFullPath(args[0]);
        if (File.Exists(path)) throw new InvalidOperationException("Self-check refuses to overwrite an existing database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Path"] = path, ["Processing:Workers"] = "2", ["Processing:RunTimeoutSeconds"] = "10",
            ["AzureOpenAI:ApiKey"] = "fixture-only-redact-this",
            ["Demo:AllowLive"] = "false", ["AllowLive"] = "true"
        }).Build();
        var settings = new ObservatorySettings(configuration, TestArchitectures.Inline);
        Check(!settings.LiveEnabled, "Explicit Demo:AllowLive=false overrides the legacy root opt-in.");
        var legacy = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowLive"] = "true"
        }).Build();
        Check(new ObservatorySettings(legacy, TestArchitectures.Inline).LiveEnabled, "Legacy root AllowLive works only when the canonical key is absent.");
        var canonical = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:AllowLive"] = "true", ["AllowLive"] = "false"
        }).Build();
        Check(new ObservatorySettings(canonical, TestArchitectures.Inline).LiveEnabled, "Canonical opt-in overrides the legacy root value.");
        Check(!new ObservatorySettings(new ConfigurationBuilder().Build(), TestArchitectures.Inline).LiveEnabled, "LIVE is disabled when both keys are absent.");
        var sanitizer = new EvidenceSanitizer(configuration);
        var data = new FixtureShop();
        CheckDefaultMode(data);
        var priced = CostCalculator.Price(new()
        {
            RunId = "fixture", Agent = "router", ModelProfileId = "gpt5", Mode = "live",
            UsageSource = "provider", InputTokens = 1000, CachedInputTokens = 400,
            OutputTokens = 100, ReasoningTokens = 30, CacheWriteTokens = 200
        }, new() { InputPerMillion = 2, CachedInputPerMillion = .5m, OutputPerMillion = 8, CacheWriteSurchargePerMillion = 1 });
        Check(priced.EstimatedCostUsd == .0024m, "Cache subtraction, additional cache-write and reasoning-subset accounting.");
        Check(CostCalculator.Price(priced with { InputTokens = null }, new()).CostStatus == "partial", "Missing usage stays partial.");
        Check(CostCalculator.Price(priced, new() { InputPerMillion = 2, OutputPerMillion = 8 }).EstimatedCostUsd is null,
            "Missing applicable prices are not zero.");
        Check(CostCalculator.Price(priced with { UsageSource = "estimated" }, new()).EstimatedCostUsd is null, "Estimated tokens are not provider usage.");
        var redacted = sanitizer.Sanitize(new
        {
            apiKey = "fixture-only-redact-this",
            exactRequestBody = """{"authorization":"Bearer privatevalue","text":"fixture-only-redact-this"}""",
            endpoint = "https://example.invalid/models?api-key=private",
            attempts = new[] { new { nested = new[] { new { secret = "private" } } } },
            inputTokens = 3
        })!.ToJsonString();
        Check(!redacted.Contains("fixture-only") && !redacted.Contains("private") && redacted.Contains("inputTokens"), "Sanitized nested bodies and URLs preserve token counters.");
        const string unmodifiedBody = "{\n  \"message\": \"exact whitespace\"  \n}";
        var unchanged = sanitizer.Sanitize(new { exactRequestBody = unmodifiedBody });
        Check(unchanged!["exactRequestBody"]!.GetValue<string>() == unmodifiedBody, "Nonsecret exact request body remains byte-for-byte intact.");
        Throws<ApiException>(() => settings.ValidateConfiguration(new() { Mode = "live", ApprovedBudgetUsd = 1 }), "LIVE disabled.");
        Throws<ApiException>(() => settings.ValidateConfiguration(new() { AgentModels = new() { ["invented"] = "gpt5" } }), "Unknown agent rejected.");
        Throws<ApiException>(() => settings.ValidateConfiguration(new() { ToolTransport = "mcp" }), "Unimplemented capability rejected.");
        CheckActiveAgentModels(configuration);
        CheckPromptConfiguration(configuration);
        IShopCatalog catalogOnly = new CatalogOnlyFixture(data);
        Check(settings.Describe(catalogOnly).CatalogHash == data.Catalog.ContentHash
              && settings.Snapshot(new() { Message = "metadata-only" }, "{}", catalogOnly).Catalog.ProductCount == data.Products.Count,
            "API metadata and snapshots require only IShopCatalog, never local business operations.");

        string persistentConversation, persistentRun, interruptedId;
        var initial = new SubmitTurnRequest
        {
            Message = "exact message \n", IdempotencyKey = "concurrent-fixture",
            Configuration = new() { PromptBlocks = new() { Checklist = true, Examples = true, ConflictingStyle = true } }
        };
        var exact = ApiJson.Serialize(initial);
        using (var store = new EvidenceStore(settings))
        {
            var conversation = store.CreateConversation("Self-check");
            persistentConversation = conversation.Id;
            var submissions = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                store.Submit(conversation.Id, settings.Snapshot(initial, exact, data)), deadline.Token)));
            persistentRun = submissions[0].Run.Id;
            Check(submissions.Select(s => s.Run.Id).Distinct().Count() == 1, "Atomic concurrent idempotency.");
            Check(store.GetConversation(conversation.Id).Messages.Count == 1, "Only one durable user message.");
            Throws<ApiException>(() => store.Submit(conversation.Id, settings.Snapshot(initial with { Message = "different" }, "{}", data)), "Idempotency conflict.");
            var changedBlocks = initial with
            {
                Configuration = initial.Configuration with { PromptBlocks = initial.Configuration.PromptBlocks with { ConflictingStyle = false } }
            };
            ThrowsApi(() => store.Submit(conversation.Id, settings.Snapshot(changedBlocks, ApiJson.Serialize(changedBlocks), data)),
                409, "idempotency_conflict");
            Check(store.GetSnapshot(persistentRun).ExactRequestBody == exact, "Exact submitted body snapshot.");
            Check(store.GetSnapshot(persistentRun).Request.Configuration.PromptBlocks == initial.Configuration.PromptBlocks
                  && store.GetRun(persistentRun).Configuration.PromptBlocks == initial.Configuration.PromptBlocks,
                "Durable run and exact request snapshot retain all five prompt-block settings.");
            Check(store.Cancel(persistentRun).Status == "cancelled", "Queued cancellation does not execute.");
            var runtime = new ProbeRuntime();
            var coordinator = new RunCoordinator(store, settings, data);
            using var worker = new RunWorker(store, coordinator, runtime, settings, sanitizer, NullLogger<RunWorker>.Instance);
            await worker.StartAsync(deadline.Token);
            var serial = store.CreateConversation("Serial");
            string Submit(string conversationId, string message, RunConfiguration? runConfiguration = null)
            {
                var request = new SubmitTurnRequest { Message = message, Configuration = runConfiguration ?? new() };
                return coordinator.Submit(conversationId, request, ApiJson.Serialize(request)).Run.Id;
            }
            var first = Submit(serial.Id, "block");
            var second = Submit(serial.Id, "second", initial.Configuration);
            await runtime.BlockStarted.Task.WaitAsync(deadline.Token);
            var parallel = Submit(store.CreateConversation("Parallel").Id, "independent");
            Check((await coordinator.WaitForCompletion(parallel, deadline.Token)).Status == "completed", "Other conversations execute independently.");
            Check(store.GetRun(second, false).Status == "queued", "Same-conversation turns remain FIFO.");
            runtime.ReleaseBlock.TrySetResult();
            Check((await coordinator.WaitForCompletion(first, deadline.Token)).Status == "completed", "First turn completes.");
            var secondRun = await coordinator.WaitForCompletion(second, deadline.Token);
            Check(secondRun.Status == "completed", "Second turn executes after first.");
            Check(runtime.Requests.Single(request => request.RunId == second).Configuration.PromptBlocks == initial.Configuration.PromptBlocks
                  && secondRun.Configuration.PromptBlocks == initial.Configuration.PromptBlocks,
                "Worker executes the selected prompt blocks from the persisted request, not a fresh default.");
            Check(store.GetConversation(serial.Id).Messages.Select(m => m.Role).SequenceEqual(["user", "assistant", "user", "assistant"]),
                "Persisted conversation order.");
            Check(runtime.Requests.Single(r => r.RunId == second).History.Select(m => m.Text).SequenceEqual(["block", "answer:block"]),
                "History contains only preceding turns without future messages.");
            var priorHistory = runtime.Requests.Single(r => r.RunId == second).History;
            Check(priorHistory.Single(message => message.Role == "assistant").ProductIds.SequenceEqual([83])
                  && priorHistory.Single(message => message.Role == "assistant").Sources.SequenceEqual(["fixture"])
                  && ApiJson.Serialize(priorHistory) == ApiJson.Serialize(store.GetConversation(serial.Id).Messages.Take(2)),
                "Trusted server-stored product references and provenance survive history forwarding without changing preceding messages.");
            Check(secondRun.Calls.Count == 1 && secondRun.Calls[0].InputTokens is null && secondRun.CostStatus == "unpriced",
                "Offline fixture calls remain separate from trace and do not invent usage.");
            var resumed = store.EventsAfter(second, secondRun.Events[0].Sequence);
            Check(resumed.Select(e => e.Id).SequenceEqual(secondRun.Events.Skip(1).Select(e => e.Id)), "Cursor replay is stable.");
            var cancel = Submit(store.CreateConversation("Cancellation").Id, "cancel");
            await runtime.CancelStarted.Task.WaitAsync(deadline.Token);
            coordinator.Cancel(cancel);
            Check((await coordinator.WaitForCompletion(cancel, deadline.Token)).Status == "cancelled", "Active cancellation reaches terminal state.");
            var cancelDrain = Submit(store.CreateConversation("Cancelled remote batch").Id, "cancel-drain");
            await runtime.DrainStarted.Task.WaitAsync(deadline.Token);
            coordinator.Cancel(cancelDrain);
            var drained = await coordinator.WaitForCompletion(cancelDrain, deadline.Token);
            Check(drained.Status == "cancelled" && drained.Calls.Count == 3 &&
                drained.Events.Count(e => e.Kind == "model.completed") == 3 &&
                drained.Events.Any(e => e.Kind == "agent.completed"),
                "User cancellation preserves every already-completed remote call during ledger drain.");
            var fail = Submit(store.CreateConversation("Failure").Id, "fail");
            var failed = await coordinator.WaitForCompletion(fail, deadline.Token);
            Check(failed.Status == "failed" && failed.Calls.Count == 1 && failed.Events.Any(e => e.Kind == "run.failed"),
                "Failures persist partial ledger and a terminal failure event, without success fallback.");
            await worker.StopAsync(deadline.Token);
            var interrupted = store.CreateConversation("Interrupted");
            interruptedId = store.Submit(interrupted.Id,
                settings.Snapshot(new() { Message = "interrupted" }, """{"message":"interrupted"}""", data)).Run.Id;
            Check(store.ClaimNext()?.Id == interruptedId, "Interrupted fixture claimed.");
        }
        using (var reopened = new EvidenceStore(settings))
        {
            reopened.RecoverInterrupted();
            Check(reopened.GetRun(interruptedId).Status == "failed", "Restart never re-executes interrupted inference.");
            var duplicate = reopened.Submit(persistentConversation, settings.Snapshot(initial, exact, data));
            Check(duplicate.Duplicate && duplicate.Run.Id == persistentRun, "Idempotency survives process restart.");
            Check(reopened.GetSnapshot(persistentRun).Request.Message == initial.Message, "Exact message survives restart.");
            Check(reopened.GetSnapshot(persistentRun).Request.Configuration.PromptBlocks == initial.Configuration.PromptBlocks
                  && reopened.GetRun(persistentRun).Configuration.PromptBlocks == initial.Configuration.PromptBlocks,
                "Prompt-block settings survive database restart in snapshot and replayable run.");
            var active = reopened.Submit(persistentConversation,
                settings.Snapshot(new() { Message = "active before clear" }, """{"message":"active before clear"}""", data)).Run.Id;
            ThrowsApi(() => reopened.ClearRunHistory(), 409, "runs_active");
            Check(reopened.Cancel(active).Status == "cancelled", "Queued run can be cancelled before history clear.");
            var experiment = new ExperimentResult("clear-check", "running", DateTimeOffset.UtcNow, null,
                [], null, "unpriced", "self-check");
            reopened.SaveExperiment(experiment.Id, new ExperimentRequest(), experiment);
            ThrowsApi(() => reopened.ClearRunHistory(), 409, "experiments_active");
            reopened.SaveExperiment(experiment.Id, null,
                experiment with { Status = "completed", CompletedAt = DateTimeOffset.UtcNow });
            var previousCount = reopened.Runs().Count;
            var deletedCount = reopened.ClearRunHistory();
            Check(deletedCount == previousCount && reopened.Runs().Count == 0,
                "Clear history removes every terminal run and its execution records.");
            ThrowsApi(() => reopened.GetExperiment(experiment.Id), 404, "not_found");
            var clearedConversation = reopened.GetConversation(persistentConversation);
            Check(clearedConversation.Messages.Count > 0
                  && clearedConversation.Messages.All(message => message.RunId is null),
                "Clear history preserves conversation text while removing references to deleted runs.");
            Check(reopened.Submit(persistentConversation, settings.Snapshot(initial, exact, data)).Run.Id != persistentRun,
                "Clearing history removes the old idempotency entry.");
        }
        await CheckAccountingDrain(path, deadline.Token);
        CheckLegacyPromptIdempotency(settings, data);
        Console.WriteLine("PASS: accounting, sanitization, validation, prompt previews/settings persistence, SQLite, atomic idempotency, FIFO, concurrency, cancellation/budget ledger drain, failures, history clearing, history isolation and recovery.");
        return 0;
    }

    private static void CheckLegacyPromptIdempotency(ObservatorySettings settings, IShopCatalog catalog)
    {
        // Frozen pre-feature payload, including its field order and escaping; never generated by Submit or RunConfiguration.
        const string legacyCanonical = """{"message":"Ordine ORD-1042: \u00E8 un difetto\n","idempotencyKey":"","configuration":{"mode":"live","modelProfileId":"gpt5","agentModels":{},"promptProfile":"good","historyStrategy":"full","toolTransport":"direct","confirmAction":false,"maxOutputTokens":1500,"maxModelCalls":24,"approvedBudgetUsd":null}}""";
        const string key = "legacy-before-prompt-blocks";
        const string at = "2026-09-23T12:00:00+00:00";
        var conversationId = Guid.NewGuid().ToString("N");
        var runId = Guid.NewGuid().ToString("N");
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyCanonical)));
        var legacyBody = legacyCanonical.Replace("\"idempotencyKey\":\"\"", $"\"idempotencyKey\":\"{key}\"", StringComparison.Ordinal);
        using var canonical = JsonDocument.Parse(legacyCanonical);
        var oldConfiguration = canonical.RootElement.GetProperty("configuration").GetRawText();
        var message = canonical.RootElement.GetProperty("message").GetRawText();
        var conversationJson = $$"""
            {"id":"{{conversationId}}","technology":"{{settings.Technology}}","title":"Legacy prompt fixture","createdAt":"{{at}}",
             "messages":[{"id":"{{Guid.NewGuid():N}}","role":"user","text":{{message}},"at":"{{at}}","runId":"{{runId}}","productIds":[],"sources":[]}]}
            """;
        var runJson = $$"""
            {"id":"{{runId}}","conversationId":"{{conversationId}}","technology":"{{settings.Technology}}","status":"cancelled",
             "message":{{message}},"configuration":{{oldConfiguration}},"startedAt":"{{at}}","completedAt":"{{at}}",
             "durationMs":null,"timeToFirstAnswerMs":null,"result":null,"error":null,"events":[],"calls":[],
             "inputTokens":null,"outputTokens":null,"estimatedCostUsd":null,"costStatus":"unpriced","scenarioId":null,"experimentId":null}
            """;
        var snapshotJson = $$"""
            {"schemaVersion":"1","request":{{legacyBody}},"exactRequestBody":{{JsonSerializer.Serialize(legacyBody)}},
             "technology":"{{settings.Technology}}","models":[],
             "catalog":{"source":"Legacy fixture","sourceUrl":"https://example.invalid/legacy","retrievedAt":"{{at}}",
                        "contentHash":"legacy-catalog-hash","productCount":0,"notice":"Synthetic legacy metadata.","imagesPolicy":"No inference images."},
             "capturedAt":"{{at}}","customerId":"CUST-DEMO-01","applicationVersion":"before-prompt-blocks"}
            """;
        Check(!legacyBody.Contains("promptBlocks", StringComparison.Ordinal)
              && !runJson.Contains("promptBlocks", StringComparison.Ordinal)
              && !snapshotJson.Contains("promptBlocks", StringComparison.Ordinal),
            "Legacy SQL fixture truly predates promptBlocks in requests, runs and snapshots.");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = settings.DatabasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false
        }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO conversations(id,created_at,record_json) VALUES($conversation,$at,$conversationJson);
                INSERT INTO runs(id,conversation_id,status,record_json,snapshot_json)
                    VALUES($run,$conversation,'cancelled',$runJson,$snapshotJson);
                INSERT INTO idempotency(conversation_id,key,request_hash,run_id) VALUES($conversation,$key,$hash,$run);
                """;
            command.Parameters.AddWithValue("$conversation", conversationId);
            command.Parameters.AddWithValue("$at", at);
            command.Parameters.AddWithValue("$conversationJson", conversationJson);
            command.Parameters.AddWithValue("$run", runId);
            command.Parameters.AddWithValue("$runJson", runJson);
            command.Parameters.AddWithValue("$snapshotJson", snapshotJson);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$hash", legacyHash);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        var omitted = ApiJson.Deserialize<SubmitTurnRequest>(legacyBody);
        var explicitFalse = omitted with { Configuration = omitted.Configuration with { PromptBlocks = new() } };
        var explicitBody = ApiJson.Serialize(explicitFalse);
        using (var document = JsonDocument.Parse(explicitBody))
        {
            var flags = document.RootElement.GetProperty("configuration").GetProperty("promptBlocks");
            Check(flags.EnumerateObject().Count() == 5 && flags.EnumerateObject().All(property => property.Value.ValueKind == JsonValueKind.False),
                "The explicit-default submission really contains all five false properties.");
        }
        Check(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ApiJson.Serialize(explicitFalse with { IdempotencyKey = "" })))) != legacyHash,
            "Legacy hash fixture detects the naive new-DTO serialization regression.");
        using (var upgraded = new EvidenceStore(settings))
        {
            Check(upgraded.GetRun(runId).Configuration.PromptBlocks == new PromptBlockSelection()
                  && upgraded.GetSnapshot(runId).Request.Configuration.PromptBlocks == new PromptBlockSelection(),
                "Old persisted run and snapshot deserialize to all-disabled selections after upgrade.");
            var count = upgraded.Runs().Count;
            foreach (var body in new[] { legacyBody, explicitBody })
            {
                var request = ApiJson.Deserialize<SubmitTurnRequest>(body);
                settings.Validate(request);
                var duplicate = upgraded.Submit(conversationId, settings.Snapshot(request, body, catalog));
                Check(duplicate.Duplicate && duplicate.Run.Id == runId && duplicate.Run.Status == "cancelled",
                    "Omitted and explicitly all-false flags both replay the pre-feature idempotency key.");
            }
            foreach (var selected in new PromptBlockSelection[]
            {
                new() { Checklist = true }, new() { OutputContract = true }, new() { Examples = true },
                new() { Redundancy = true }, new() { ConflictingStyle = true }
            })
            {
                var changed = omitted with { Configuration = omitted.Configuration with { PromptBlocks = selected } };
                ThrowsApi(() => upgraded.Submit(conversationId, settings.Snapshot(changed, ApiJson.Serialize(changed), catalog)),
                    409, "idempotency_conflict");
            }
            Check(upgraded.Runs().Count == count && upgraded.GetConversation(conversationId).Messages.Count == 1
                  && upgraded.GetRun(runId).Calls.Count == 0 && upgraded.GetRun(runId).Events.Count == 0
                  && upgraded.GetSnapshot(runId).ExactRequestBody == legacyBody,
                "Legacy duplicates and conflicts do not rewrite provenance, append messages or execute a run.");
            var modernExplicit = explicitFalse with { IdempotencyKey = "new-explicit-defaults" };
            var modern = upgraded.Submit(conversationId, settings.Snapshot(modernExplicit, ApiJson.Serialize(modernExplicit), catalog));
            var modernOmittedBody = legacyBody.Replace(key, modernExplicit.IdempotencyKey, StringComparison.Ordinal);
            var modernDuplicate = upgraded.Submit(conversationId,
                settings.Snapshot(ApiJson.Deserialize<SubmitTurnRequest>(modernOmittedBody), modernOmittedBody, catalog));
            Check(!modern.Duplicate && modernDuplicate.Duplicate && modernDuplicate.Run.Id == modern.Run.Id
                  && upgraded.GetConversation(conversationId).Messages.Count == 2,
                "A new explicit-all-false submission also deduplicates a later omitted-field submission.");
            upgraded.Cancel(modern.Run.Id);
        }
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT r.record_json,r.snapshot_json,i.request_hash FROM runs r
                JOIN idempotency i ON i.run_id=r.id WHERE r.id=$run;
                """;
            command.Parameters.AddWithValue("$run", runId);
            using var reader = command.ExecuteReader();
            Check(reader.Read() && reader.GetString(0) == runJson && reader.GetString(1) == snapshotJson && reader.GetString(2) == legacyHash,
                "Original legacy JSON and independently seeded canonical hash remain byte-for-byte unchanged.");
        }
        Console.WriteLine("PASS legacy prompt idempotency: independently seeded old hash/run/snapshot, omitted/false aliases, five enabled-flag conflicts, no record rewrite.");
    }

    private static async Task CheckAccountingDrain(string path, CancellationToken token)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Path"] = path, ["Demo:AllowLive"] = "true",
            ["AzureOpenAI:Endpoint"] = "https://example.invalid",
            ["Models:gpt5:Deployment"] = "fixture-only", ["Models:gpt5:InputPerMillion"] = "1",
            ["Models:gpt5:CachedInputPerMillion"] = "1", ["Models:gpt5:OutputPerMillion"] = "1",
            ["Models:gpt5:SourceUrl"] = "https://example.invalid/fixture-pricing",
            ["Models:gpt5:VerifiedAt"] = "2026-09-23",
            ["Models:gpt5:Capabilities:FunctionCalling"] = "true", ["Models:gpt5:Capabilities:MaxOutputTokens"] = "true"
        }).Build();
        var settings = new ObservatorySettings(configuration, TestArchitectures.Inline);
        using var store = new EvidenceStore(settings);
        var coordinator = new RunCoordinator(store, settings, new FixtureShop());
        var runtime = new CompletedBatchFixture();
        using var worker = new RunWorker(store, coordinator, runtime, settings, new(configuration), NullLogger<RunWorker>.Instance);
        await worker.StartAsync(token);
        var cases = new[]
        {
            (Message: "budget-drain", Budget: .001m, Limit: 24, CancelAfter: 1, Error: "budget reached"),
            (Message: "limit-drain", Budget: 10m, Limit: 1, CancelAfter: 2, Error: "model-call limit"),
            (Message: "usage-drain", Budget: 10m, Limit: 24, CancelAfter: 1, Error: "pricing is incomplete"),
            (Message: "failure-drain", Budget: 10m, Limit: 24, CancelAfter: 0, Error: "Intentional drained provider failure")
        };
        foreach (var item in cases)
        {
            var request = new SubmitTurnRequest
            {
                Message = item.Message,
                Configuration = new() { Mode = "live", ApprovedBudgetUsd = item.Budget, MaxModelCalls = item.Limit }
            };
            var id = coordinator.Submit(store.CreateConversation(item.Message).Id, request, ApiJson.Serialize(request)).Run.Id;
            var run = await coordinator.WaitForCompletion(id, token);
            Check(run.Status == "failed" && run.Error?.Contains(item.Error, StringComparison.Ordinal) == true,
                $"{item.Message}: preserve the actual stop/failure reason.");
            Check(run.Calls.Count == 3 && run.Events.Count(e => e.Kind == "model.completed") == 3 &&
                run.Events.Any(e => e.Kind == "protocol.response"),
                $"{item.Message}: all captured remote calls must reach the independent ledger and timeline.");
            Check(runtime.CancellationAfterCall[id] == item.CancelAfter,
                $"{item.Message}: cancellation occurs at the exact threshold without throwing from emit.");
            Check(item.Message is "usage-drain" or "failure-drain"
                ? run.EstimatedCostUsd is null && run.CostStatus == "partial"
                : run.EstimatedCostUsd == .003m, $"{item.Message}: aggregate the full drained batch without invented usage.");
        }
        await worker.StopAsync(token);
    }

    private static void CheckActiveAgentModels(IConfiguration configuration)
    {
        foreach (var technology in DemoTechnologies.All)
        {
            var selected = new ConfigurationBuilder().AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Technology"] = technology }).Build();
            var settings = new ObservatorySettings(selected, TestArchitectures.For(technology));
            string[] expected = technology == DemoTechnologies.A2A ? ["router", "catalog", "orders", "returns"] : ["router"];
            var capabilities = JsonSerializer.SerializeToElement(settings.Capabilities, ApiJson.Options);
            Check(capabilities.GetProperty("agentNames").EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected),
                $"{technology} API advertises only active, model-bearing agents.");
            // Agent/model validation only: LIVE authorization is covered by CheckPromptConfiguration.
            settings.ValidatePromptPreview(new()
            {
                AgentModels = expected.ToDictionary(agent => agent, _ => "gpt6-luna", StringComparer.Ordinal)
            });
            settings.ValidatePromptPreview(new() { AgentModels = new() { ["router"] = "gpt6-astra" } });
            Throws<ApiException>(() => settings.ValidatePromptPreview(new()
            {
                AgentModels = new() { ["router"] = "invented-model" }
            }), $"{technology} rejects unregistered active models.");
            if (technology != DemoTechnologies.A2A)
                foreach (var role in new[] { "catalog", "orders", "returns" })
                    Throws<ApiException>(() => settings.ValidatePromptPreview(new()
                    {
                        AgentModels = new() { [role] = "gpt5" }
                    }), $"{technology} rejects inactive {role} model overrides, even with a valid model.");
        }
    }

    private static void CheckPromptConfiguration(IConfiguration configuration)
    {
        var legacy = ApiJson.Deserialize<RunConfiguration>("""{"mode":"live","promptProfile":"good"}""");
        Check(legacy.PromptBlocks == new PromptBlockSelection(), "Legacy JSON omitting promptBlocks preserves the disabled baseline.");
        var legacyRun = ApiJson.Deserialize<RunRecord>(
            """{"id":"legacy","conversationId":"legacy","technology":"inline","configuration":{"promptProfile":"bad"}}""");
        Check(legacyRun.Configuration.PromptBlocks == new PromptBlockSelection(),
            "Older stored run JSON is replayable without adding selected blocks.");
        for (var mask = 0; mask < 32; mask++)
        {
            var original = new RunConfiguration
            {
                PromptBlocks = new()
                {
                    Checklist = (mask & 1) != 0, OutputContract = (mask & 2) != 0, Examples = (mask & 4) != 0,
                    Redundancy = (mask & 8) != 0, ConflictingStyle = (mask & 16) != 0
                }
            };
            Check(ApiJson.Deserialize<RunConfiguration>(ApiJson.Serialize(original)).PromptBlocks == original.PromptBlocks,
                $"Prompt selection {mask} round-trips without dropping enabled or disabled fields.");
            var copy = original with { PromptBlocks = original.PromptBlocks with { Checklist = !original.PromptBlocks.Checklist } };
            Check(copy.PromptBlocks != original.PromptBlocks && original.PromptBlocks.Checklist == ((mask & 1) != 0),
                "A copied immutable selection does not mutate the original run configuration.");
        }
        foreach (var technology in DemoTechnologies.All)
        {
            var config = new ConfigurationBuilder().AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Technology"] = technology }).Build();
            var settings = new ObservatorySettings(config, TestArchitectures.For(technology));
            var live = new RunConfiguration
            {
                Mode = "live", PromptProfile = "bad",
                AgentModels = new() { ["router"] = "gpt6-luna" }, PromptBlocks = new() { ConflictingStyle = true }
            };
            settings.ValidatePromptPreview(live);
            Check(TestArchitectures.Preview(technology, live).Agents.All(agent => agent.Instructions.Contains("<conflicting_style>", StringComparison.Ordinal)),
                "LIVE preview remains side-effect-free and available without budget, deployment or LIVE opt-in.");
            ThrowsApi(() => settings.ValidateConfiguration(live), 403, "live_disabled");
            var nullBlocks = ApiJson.Deserialize<RunConfiguration>("""{"promptBlocks":null}""");
            var invalid = new (RunConfiguration Value, string Code)[]
            {
                (nullBlocks, "invalid_prompt_blocks"),
                (new() { Mode = "invented" }, "invalid_mode"),
                (new() { PromptProfile = "invented" }, "invalid_prompt_profile"),
                (new() { HistoryStrategy = "invented" }, "invalid_history_strategy"),
                (new() { ToolTransport = "mcp" }, "invalid_tool_transport"),
                (new() { MaxModelCalls = 0 }, "invalid_call_limit"),
                (new() { MaxModelCalls = 65 }, "invalid_call_limit"),
                (new() { MaxOutputTokens = 0 }, "invalid_output_limit"),
                (new() { MaxOutputTokens = 16385 }, "invalid_output_limit"),
                (new() { AgentModels = null! }, "invalid_agent_models"),
                (new() { AgentModels = new() { ["invented"] = "gpt5" } }, "invalid_agent_name"),
                (new() { AgentModels = new() { ["router"] = "invented" } }, "invalid_model_profile"),
                (new() { ModelProfileId = "invented" }, "invalid_model_profile"),
                (new() { ApprovedBudgetUsd = 0 }, "invalid_budget"),
                (new() { ApprovedBudgetUsd = settings.MaxApprovedBudgetUsd + 1 }, "invalid_budget")
            };
            foreach (var item in invalid)
            {
                ThrowsApi(() => settings.ValidatePromptPreview(item.Value), 400, item.Code);
                ThrowsApi(() => settings.ValidateConfiguration(item.Value), 400, item.Code);
            }
            if (technology != DemoTechnologies.A2A)
                ThrowsApi(() => settings.ValidatePromptPreview(new() { AgentModels = new() { ["catalog"] = "gpt5" } }),
                    400, "invalid_agent_name");
            var liveEnabled = new ObservatorySettings(new ConfigurationBuilder().AddConfiguration(config)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:AllowLive"] = "true" }).Build(),
                TestArchitectures.For(technology));
            liveEnabled.ValidatePromptPreview(live);
            liveEnabled.ValidatePromptPreview(live with { ApprovedBudgetUsd = 1 });
            ThrowsApi(() => liveEnabled.ValidateConfiguration(live), 422, "budget_required");
            ThrowsApi(() => liveEnabled.ValidateConfiguration(live with { ApprovedBudgetUsd = 1 }), 422, "deployment_required");
        }
    }

    private static void ThrowsApi(Action action, int status, string code)
    {
        try { action(); }
        catch (ApiException exception)
        {
            Check(exception.Status == status && exception.Code == code, $"Expected {status}/{code}, got {exception.Status}/{exception.Code}.");
            return;
        }
        throw new InvalidOperationException($"SELF-CHECK FAILED: expected {status}/{code}.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("SELF-CHECK FAILED: " + name);
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("SELF-CHECK FAILED: " + name);
    }

    private sealed class ProbeRuntime : IAgentRuntime
    {
        public ConcurrentBag<AgentRunRequest> Requests { get; } = [];
        public TaskCompletionSource BlockStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DrainStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Message == "block")
            {
                BlockStarted.TrySetResult();
                await ReleaseBlock.Task.WaitAsync(cancellationToken);
            }
            if (request.Message == "cancel")
            {
                CancelStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (request.Message == "cancel-drain")
            {
                DrainStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                for (var i = 0; i < 3; i++)
                    await emit(new()
                    {
                        RunId = request.RunId, Kind = "model.completed",
                        Data = new ModelCallRecord { RunId = request.RunId, Agent = "router", ModelProfileId = "gpt5", Mode = "live", UsageSource = "fixture" }
                    });
                await emit(new() { RunId = request.RunId, Kind = "agent.completed", Agent = "router" });
                throw new OperationCanceledException(cancellationToken);
            }
            await emit(new()
            {
                RunId = request.RunId, Kind = "model.completed",
                Data = new ModelCallRecord
                {
                    RunId = request.RunId, Agent = "router", ModelProfileId = "gpt5",
                    Mode = "live", UsageSource = "fixture",
                    Status = request.Message == "fail" ? "failed" : "completed"
                }
            });
            if (request.Message == "fail") throw new InvalidOperationException("Intentional fixture failure.");
            await emit(new() { RunId = request.RunId, Kind = "answer.delta", Message = "answer:" + request.Message });
            return new("answer:" + request.Message, [83], ["fixture"]);
        }
    }

    private sealed class CompletedBatchFixture : IAgentRuntime
    {
        public ConcurrentDictionary<string, int> CancellationAfterCall { get; } = new();

        public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit, CancellationToken cancellationToken = default)
        {
            // Pure local fixture: simulate a batch captured before any API cancellation; no SDK/network exists here.
            if (request.Message == "unbounded-delay") await Task.Delay(1500, cancellationToken);
            var captured = Enumerable.Range(0, 3).Select(i =>
            {
                var failed = request.Message == "failure-drain" && i == 2;
                var missingUsage = failed || request.Message == "usage-drain" && i == 0;
                return new ModelCallRecord
                {
                    RunId = request.RunId, Agent = "router", ModelProfileId = "gpt5", Mode = "live",
                    UsageSource = "provider", Status = failed ? "failed" : "completed",
                    InputTokens = missingUsage ? null : 1000, OutputTokens = missingUsage ? null : 0,
                    CachedInputTokens = missingUsage ? null : 0, Error = failed ? "Intentional drained provider failure." : null,
                    Request = new { fixtureOnly = true, capturedIndex = i }
                };
            }).ToArray();
            CancellationAfterCall[request.RunId] = 0;
            var count = 0;
            foreach (var call in captured)
            {
                count++;
                await emit(new() { RunId = request.RunId, Kind = "model.completed", Data = call });
                if (cancellationToken.IsCancellationRequested && CancellationAfterCall[request.RunId] == 0)
                    CancellationAfterCall[request.RunId] = count;
            }
            await emit(new() { RunId = request.RunId, Kind = "protocol.response", Data = new { fixtureOnly = true } });
            if (request.Message == "failure-drain")
                throw new InvalidOperationException("Intentional drained provider failure.");
            return new("Local fixture batch drained.", [], []);
        }
    }

    private sealed class CatalogOnlyFixture(IShopCatalog inner) : IShopCatalog
    {
        public CatalogSnapshot Catalog => inner.Catalog;
        public IReadOnlyList<Product> Products => inner.Products;
        public IReadOnlyList<ScenarioDefinition> Scenarios => inner.Scenarios;
    }

    private sealed class FixtureShop : IShopData
    {
        public CatalogSnapshot Catalog { get; } = new() { Source = "self-check", SourceUrl = "https://example.invalid/catalog" };
        public IReadOnlyList<Product> Products => [];
        public IReadOnlyList<ShopOrder> DemoOrders => [];
        public IReadOnlyList<ShopPolicy> Policies => [];
        public IReadOnlyList<ScenarioDefinition> Scenarios => [];
        public IReadOnlyList<ProductFact> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5) => [];
        public CatalogQueryResponse QueryCatalog(CatalogQueryRequest query) => throw new NotSupportedException();
        public CatalogFacetsResponse GetCatalogFacets() => throw new NotSupportedException();
        public ProductFact GetProduct(int productId) => throw new NotSupportedException();
        public ShopOrder GetOrder(string orderId, string customerId = DemoClock.CustomerId) => throw new NotSupportedException();
        public ReturnAssessment AssessReturn(string orderId, string reason, string customerId = DemoClock.CustomerId) => throw new NotSupportedException();
        public ReturnDraft CreateReturnDraft(string orderId, string reason, bool confirmed, string customerId = DemoClock.CustomerId) => throw new NotSupportedException();
    }
}
