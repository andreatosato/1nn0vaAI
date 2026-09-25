using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

public static class ApiEndpoints
{
    public static void MapObservatory(this WebApplication app)
    {
        app.MapGet("/api/config", (ObservatorySettings settings, IShopCatalog data) =>
        {
            var node = JsonSerializer.SerializeToNode(settings.Describe(data), ApiJson.Options)!.AsObject();
            node["capabilities"] = JsonSerializer.SerializeToNode(settings.Capabilities, ApiJson.Options);
            node["promptBlocks"] = JsonSerializer.SerializeToNode(PromptLaboratory.Blocks, ApiJson.Options);
            return Results.Json(node, ApiJson.Options);
        });
        app.MapPost("/api/prompts/preview", async (HttpRequest http, ObservatorySettings settings) =>
        {
            var (configuration, _) = await ReadBody<RunConfiguration>(http);
            settings.ValidatePromptPreview(configuration);
            return TypedResults.Ok(PromptLaboratory.Preview(settings.Technology, configuration));
        })
            .WithName("PreviewPromptInstructions")
            .WithSummary("Preview configured instructions without invoking models or tools.")
            .WithDescription("Returns the same base and optional instructions used by each active agent. Runtime history, tools and native skill context are not part of this preview. Does not authorize LIVE execution or persist state.")
            .Accepts<RunConfiguration>("application/json")
            .ProducesProblem(400)
            .ProducesProblem(413)
            .ProducesProblem(415)
            .ProducesProblem(422);
        app.MapGet("/api/products", (IShopCatalog data) => Results.Ok(data.Products));
        app.MapGet("/api/demo-data", async ([Microsoft.AspNetCore.Mvc.FromServices] ShopServiceClient services, CancellationToken token) =>
            TypedResults.Ok(await services.GetDemoDataAsync(token)))
            .WithName("GetDemoData")
            .WithSummary("Inspect synthetic orders and policies without invoking agents or creating state.")
            .WithDescription("UI-only teaching snapshot from Orders and Returns over HTTP, including other demo customers. Not customer authorization or an agent tool; chat access and confirmation guards are unchanged. Unavailable or invalid upstream data fails explicitly without local fallback.")
            .ProducesProblem(StatusCodes.Status502BadGateway);
        app.MapGet("/api/scenarios", (IShopCatalog data) => Results.Ok(data.Scenarios));
        app.MapGet("/api/conversations", (EvidenceStore store) => Results.Ok(store.Conversations()));
        app.MapPost("/api/conversations", async (HttpRequest http, EvidenceStore store) =>
        {
            var (body, _) = await ReadBody<CreateConversationRequest>(http, new());
            var conversation = store.CreateConversation(body.Title);
            return Results.Created($"/api/conversations/{conversation.Id}", conversation);
        });
        app.MapGet("/api/conversations/{id}", (string id, EvidenceStore store) =>
            Results.Ok(store.GetConversation(ObservatorySettings.ValidateId(id))));
        app.MapPost("/api/conversations/{id}/turns", async (string id, HttpRequest http, HttpResponse response, RunCoordinator runs) =>
        {
            id = ObservatorySettings.ValidateId(id);
            var (request, exactBody) = await ReadBody<SubmitTurnRequest>(http);
            var headerKey = http.Headers["Idempotency-Key"].ToString();
            if (headerKey.Length > 0)
            {
                using var doc = JsonDocument.Parse(exactBody);
                var bodyHasKey = doc.RootElement.EnumerateObject().Any(p =>
                    string.Equals(p.Name, "idempotencyKey", StringComparison.OrdinalIgnoreCase));
                if (bodyHasKey && request.IdempotencyKey != headerKey)
                    throw new ApiException(400, "idempotency_mismatch", "Header and body idempotency keys differ.");
                request = request with { IdempotencyKey = headerKey };
            }
            var submission = runs.Submit(id, request, exactBody);
            response.Headers["Idempotency-Replayed"] = submission.Duplicate ? "true" : "false";
            var accepted = new TurnAccepted(submission.Run.Id, id, $"/api/runs/{submission.Run.Id}/events");
            return Results.Accepted($"/api/runs/{submission.Run.Id}", accepted);
        });
        app.MapGet("/api/runs", (EvidenceStore store) => Results.Ok(store.Runs()));
        app.MapDelete("/api/runs", (EvidenceStore store) =>
            Results.Ok(new { deletedRuns = store.ClearRunHistory() }))
            .WithName("ClearRunHistory")
            .WithSummary("Clear completed run history for this demo.")
            .WithDescription("Removes run events, model-call records and experiment reports. Conversation text and metadata remain, but messages are detached from deleted runs. Active runs or experiments block the operation.")
            .ProducesProblem(StatusCodes.Status409Conflict);
        app.MapGet("/api/runs/{id}", (string id, EvidenceStore store) =>
            Results.Ok(store.GetRun(ObservatorySettings.ValidateId(id))));
        app.MapGet("/api/runs/{id}/events", (string id, HttpContext context, EvidenceStore store) =>
            StreamEvents(ObservatorySettings.ValidateId(id), context, store, replayOnly: false));
        app.MapPost("/api/runs/{id}/replay", async (string id, HttpContext context, EvidenceStore store, EvidenceSanitizer sanitizer) =>
        {
            id = ObservatorySettings.ValidateId(id);
            var acceptsSse = context.Request.GetTypedHeaders().Accept?.Any(value =>
                value.MediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) && value.Quality is not <= 0) == true;
            if (acceptsSse)
            {
                await StreamEvents(id, context, store, replayOnly: true);
                return;
            }
            var original = store.GetRun(id);
            var replay = sanitizer.Sanitize(original)!.AsObject();
            replay["replayOnly"] = true;
            replay["originalRunId"] = id;
            replay["replayThroughSequence"] = original.Events.LastOrDefault()?.Sequence ?? 0;
            replay["notice"] = "Original stored run and events only. No new run, model invocation or tool execution.";
            context.Response.Headers["X-Original-Run-Id"] = id;
            context.Response.Headers["X-Replay-Only"] = "true";
            await Results.Json(replay, ApiJson.Options).ExecuteAsync(context);
        });
        app.MapPost("/api/runs/{id}/cancel", (string id, RunCoordinator runs) =>
            Results.Ok(runs.Cancel(ObservatorySettings.ValidateId(id))));
        app.MapGet("/api/runs/{id}/export", (string id, EvidenceStore store, EvidenceSanitizer sanitizer) =>
        {
            id = ObservatorySettings.ValidateId(id);
            var run = store.GetRun(id);
            var snapshot = store.GetSnapshot(id);
            var bundle = sanitizer.Sanitize(new
            {
                schemaVersion = "1", exportedAt = DateTimeOffset.UtcNow, originalRunId = id,
                sanitized = true, replayOnly = false,
                provenance = snapshot,
                run = run with { Calls = [], Events = [] },
                ledger = new
                {
                    calls = run.Calls,
                    totals = new
                    {
                        modelCallCount = run.Calls.Count, run.InputTokens, run.OutputTokens,
                        run.EstimatedCostUsd, run.CostStatus,
                        knownPricedSubtotalUsd = run.Calls.Any(c => c.EstimatedCostUsd.HasValue)
                            ? run.Calls.Where(c => c.EstimatedCostUsd.HasValue).Sum(c => c.EstimatedCostUsd) : null
                    }
                },
                timeline = run.Events,
                accounting = new
                {
                    source = "Unique persisted ModelCallRecord IDs from model.completed; never trace-span totals.",
                    cachedInput = "Subset of input: charged cached input is subtracted from ordinary input.",
                    reasoning = "Subset of output: never added to output a second time.",
                    cacheWrite = "Configured replacement cache-write rates use a disjoint input bucket; legacy explicitly configured surcharges remain additive. Tiered pricing requires provider-reported cache-write usage.",
                    contextTier = "Input tokens alone select the request-wide tier; long rates apply only above the configured threshold. Reasoning remains a subset of output.",
                    incomplete = "Missing usage or rates stays null/partial/unpriced.",
                    traceWarning = "Timeline model.completed data references the same calls; do not sum it with the ledger."
                },
                replay = new { originalRunId = id, eventsUrl = $"/api/runs/{id}/events", method = "POST", url = $"/api/runs/{id}/replay", executesModels = false }
            });
            var bytes = Encoding.UTF8.GetBytes(bundle!.ToJsonString(new(ApiJson.Options) { WriteIndented = true }));
            return Results.File(bytes, "application/json", $"observatory-{run.Technology}-{id}.json");
        });
        app.MapPost("/api/experiments", async (HttpRequest http, ExperimentService experiments) =>
        {
            var (request, _) = await ReadBody<ExperimentRequest>(http, new());
            return request.DryRun ? Results.Ok(experiments.Estimate(request))
                : Results.Ok(await experiments.Execute(request, http.HttpContext.RequestAborted));
        });
        app.MapGet("/api/experiments/{id}", (string id, EvidenceStore store) =>
            Results.Ok(store.GetExperiment(ObservatorySettings.ValidateId(id))));
    }

    private static async Task<(T Value, string ExactBody)> ReadBody<T>(HttpRequest request, T? emptyDefault = default)
    {
        if (request.ContentLength > 65536) throw new ApiException(413, "body_too_large", "JSON body must not exceed 64 KiB.");
        if (request.ContentLength is > 0 && !request.HasJsonContentType())
            throw new ApiException(415, "json_required", "Use Content-Type: application/json.");
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var buffer = new char[65537];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count), request.HttpContext.RequestAborted);
            if (read == 0) break;
            count += read;
        }
        if (count > 65536) throw new ApiException(413, "body_too_large", "JSON body must not exceed 64 KiB.");
        var text = new string(buffer, 0, count);
        if (string.IsNullOrWhiteSpace(text) && emptyDefault is not null) return (emptyDefault, "{}");
        if (!request.HasJsonContentType())
            throw new ApiException(415, "json_required", "Use Content-Type: application/json.");
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ApiException(400, "invalid_json", "A JSON object is required.");
            RejectDuplicateProperties(document.RootElement);
            return (JsonSerializer.Deserialize<T>(text, ApiJson.Requests)
                ?? throw new ApiException(400, "invalid_json", "A JSON object is required."), text);
        }
        catch (JsonException exception)
        {
            throw new ApiException(400, "invalid_json", $"Invalid JSON request at {exception.Path ?? "$"}. Unknown properties are not accepted.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                    throw new ApiException(400, "duplicate_property", "JSON objects must not contain duplicate property names.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static async Task StreamEvents(string id, HttpContext context, EvidenceStore store, bool replayOnly)
    {
        var run = store.GetRun(id);
        var after = store.ResolveEventCursor(id, context.Request.Headers["Last-Event-ID"].ToString());
        var replayThrough = run.Events.LastOrDefault()?.Sequence ?? 0;
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers["X-Original-Run-Id"] = id;
        response.Headers["X-Replay-Only"] = replayOnly ? "true" : "false";
        if (replayOnly) response.Headers["X-Replay-Through-Sequence"] = replayThrough.ToString(CultureInfo.InvariantCulture);
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.StartAsync(context.RequestAborted);
        try
        {
            var heartbeat = DateTimeOffset.UtcNow;
            while (!context.RequestAborted.IsCancellationRequested)
            {
                var events = store.EventsAfter(id, after)
                    .Where(e => !replayOnly || e.Sequence <= replayThrough).ToArray();
                foreach (var item in events)
                {
                    await response.WriteAsync($"id: {item.Sequence.ToString(CultureInfo.InvariantCulture)}\ndata: {ApiJson.Serialize(item)}\n\n",
                        context.RequestAborted);
                    after = item.Sequence;
                }
                if (events.Length > 0) await response.Body.FlushAsync(context.RequestAborted);
                if (replayOnly)
                {
                    if (after >= replayThrough || events.Length == 0) break;
                    continue;
                }
                if (events.Length > 0) continue;
                if (EvidenceStore.IsTerminal(store.GetRun(id, false).Status))
                {
                    // Re-read after the terminal-state read so the final event cannot be lost to a completion race.
                    if (store.EventsAfter(id, after, 1).Count == 0) break;
                    continue;
                }
                if (DateTimeOffset.UtcNow - heartbeat > TimeSpan.FromSeconds(10))
                {
                    await response.WriteAsync(": heartbeat\n\n", context.RequestAborted);
                    await response.Body.FlushAsync(context.RequestAborted);
                    heartbeat = DateTimeOffset.UtcNow;
                }
                await Task.Delay(100, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    }
}
