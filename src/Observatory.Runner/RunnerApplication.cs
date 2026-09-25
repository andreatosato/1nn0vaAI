using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Observatory.Core;

namespace Observatory.Runner;

public static class RunnerApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> Flags = ["--help"];
    private static readonly HashSet<string> Options =
    [
        "--base-url", "--artifacts", "--models", "--prompt", "--history", "--transport",
        "--max-calls", "--max-output", "--budget-usd", "--repetitions", "--scenarios", "--run-id", "--timeout-seconds",
        "--configurations"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("""
                Observatory.Runner <smoke|dryrun|benchmark|export> --base-url URL --artifacts DIRECTORY
                  --models gpt5,gpt6-astra,gpt6-sol,gpt6-luna  (default: gpt5)
                  --scenarios main-six-turns --repetitions 1
                  --prompt good --history full --transport direct
                  --max-calls 24 --max-output 1500 --timeout-seconds 180
                  --configurations JSON  Explicit RunConfiguration array instead of individual configuration flags.
                  export --run-id ID     Save the API's sanitized evidence bundle.

                smoke checks health and demo data without sending a chat turn.
                dryrun and benchmark only calculate plans; they never call providers.
                Batch inference is disabled; authorize individual LIVE turns in the UI.
                Artifacts must be explicitly selected; no credentials or endpoints with credentials are accepted.
                """);
            return 0;
        }
        try
        {
            var command = args[0];
            if (command is not ("smoke" or "dryrun" or "benchmark" or "export"))
                throw new ArgumentException("Command must be smoke, dryrun, benchmark or export.");
            var options = Parse(args.Skip(1).ToArray());
            var baseUrl = Required(options, "--base-url");
            if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("--base-url must be an HTTP(S) URL without credentials, query or fragment.");
            var artifacts = Path.GetFullPath(Required(options, "--artifacts"));
            var timeout = Integer(options, "--timeout-seconds", 180, 1, 3600);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(timeout) };

            return command switch
            {
                "smoke" => await Smoke(http, options, artifacts, deadline.Token),
                "dryrun" or "benchmark" => await Experiment(http, options, artifacts, deadline.Token),
                "export" => await Export(http, options, artifacts, deadline.Token),
                _ => 1
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Runner timed out or was cancelled. Inspect stored runs through the API; no retry was performed.");
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or HttpRequestException or JsonException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> Smoke(HttpClient http, Dictionary<string, string> options, string artifacts, CancellationToken token)
    {
        using (var health = await http.GetAsync("health", token)) health.EnsureSuccessStatusCode();
        var config = await Get<DemoConfiguration>(http, "api/config", token);
        Require(config.DefaultMode == "live", "API must advertise LIVE as its only supported execution mode.");
        var products = await Get<Product[]>(http, "api/products", token);
        var scenarios = await Get<ScenarioDefinition[]>(http, "api/scenarios", token);
        Require(products.Length > 0 && scenarios.Length > 0, "Catalog and scenarios must be available.");
        var report = new
        {
            command = "smoke", passed = true, mode = "live", config.Technology,
            liveReady = config.AllowLive, productCount = products.Length, scenarioCount = scenarios.Length,
            checks = new[] { "health", "live-only-configuration", "catalog", "scenario" },
            notice = "No chat turns were submitted; no provider was called."
        };
        var path = await Save(artifacts, $"smoke-{config.Technology}", report, token);
        Console.WriteLine($"PASS LIVE configuration {config.Technology}: {products.Length} products, {scenarios.Length} scenarios. No inference. Report: {path}");
        return 0;
    }

    private static async Task<int> Experiment(HttpClient http, Dictionary<string, string> options, string artifacts, CancellationToken token)
    {
        var configurations = Configurations(options);
        var request = new ExperimentRequest
        {
            ScenarioIds = Csv(options.GetValueOrDefault("--scenarios", "main-six-turns")),
            Configurations = configurations, Repetitions = Integer(options, "--repetitions", 1, 1, 10),
            DryRun = true
        };
        var result = await Post<JsonElement>(http, "api/experiments", request, token);
        var path = await Save(artifacts, "dryrun", result, token);
        Require(result.GetProperty("dryRun").GetBoolean(), "Server unexpectedly executed an experiment.");
        Console.WriteLine($"DRY RUN: {result.GetProperty("turnCount")} scenario turns, {result.GetProperty("estimatedModelCalls")} maximum configured model calls. No inference. {path}");
        foreach (var item in result.GetProperty("cases").EnumerateArray())
        foreach (var runId in item.GetProperty("runIds").EnumerateArray().Select(e => e.GetString()!))
        {
            var bundle = await Get<JsonElement>(http, $"api/runs/{runId}/export", token);
            ValidateBundle(bundle, runId);
            await Save(artifacts, $"run-{runId}", bundle, token);
        }
        return failed == 0 ? 0 : 2;
    }

    private static async Task<int> Export(HttpClient http, Dictionary<string, string> options, string artifacts, CancellationToken token)
    {
        var id = Required(options, "--run-id");
        if (!Guid.TryParseExact(id, "N", out var parsed)) throw new ArgumentException("--run-id must contain 32 hexadecimal characters.");
        id = parsed.ToString("N");
        var bundle = await Get<JsonElement>(http, $"api/runs/{id}/export", token);
        ValidateBundle(bundle, id);
        Console.WriteLine(await Save(artifacts, $"run-{id}", bundle, token));
        return 0;
    }

    private static void ValidateBundle(JsonElement bundle, string id)
    {
        Require(bundle.TryGetProperty("sanitized", out var sanitized) && sanitized.GetBoolean(),
            "Refusing to save an export not marked sanitized.");
        Require(bundle.GetProperty("originalRunId").GetString() == id, "Export refers to the wrong original run.");
    }

    private static async Task<List<RunEvent>> Events(HttpClient http, string id, bool replay, long? after, CancellationToken token)
    {
        using var request = new HttpRequestMessage(replay ? HttpMethod.Post : HttpMethod.Get,
            $"api/runs/{id}/{(replay ? "replay" : "events")}");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (after is not null) request.Headers.Add("Last-Event-ID", after.Value.ToString(CultureInfo.InvariantCulture));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await EnsureSuccess(response, token);
        if (replay)
        {
            Require(response.Headers.TryGetValues("X-Original-Run-Id", out var original) && original.Single() == id, "Replay must identify the original run.");
            Require(response.Headers.TryGetValues("X-Replay-Only", out var flags) && flags.Single() == "true", "Replay must explicitly be replay-only.");
        }
        Require(response.Content.Headers.ContentType?.MediaType == "text/event-stream", "SSE Content-Type is missing.");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var result = new List<RunEvent>();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
                throw new InvalidOperationException("Run SSE must use default message events, not named events.");
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            result.Add(JsonSerializer.Deserialize<RunEvent>(line[5..].TrimStart(), Json)
                ?? throw new InvalidOperationException("SSE event was null."));
        }
        return result;
    }

    private static async Task<RunRecord> Wait(HttpClient http, string id, CancellationToken token)
    {
        while (true)
        {
            var run = await Get<RunRecord>(http, $"api/runs/{id}", token);
            if (run.Status is "completed" or "failed" or "cancelled") return run;
            await Task.Delay(100, token);
        }
    }

    private static async Task<AcceptedTurn> Submit(HttpClient http, string conversationId, SubmitTurnRequest request, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync($"api/conversations/{conversationId}/turns", request, Json, token);
        await EnsureSuccess(response, token);
        Require(response.StatusCode == HttpStatusCode.Accepted, "Turn submission must return HTTP 202.");
        return await response.Content.ReadFromJsonAsync<AcceptedTurn>(Json, token) ?? throw new InvalidOperationException("Empty accepted response.");
    }

    private static async Task<T> Get<T>(HttpClient http, string route, CancellationToken token)
    {
        using var response = await http.GetAsync(route, token);
        await EnsureSuccess(response, token);
        return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new InvalidOperationException("Empty API response.");
    }

    private static async Task<T> Post<T>(HttpClient http, string route, object request, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(route, request, Json, token);
        await EnsureSuccess(response, token);
        return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new InvalidOperationException("Empty API response.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(token);
        throw new HttpRequestException($"API HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 2000)]}");
    }

    private static RunConfiguration[] Configurations(Dictionary<string, string> options)
    {
        if (options.TryGetValue("--configurations", out var json))
        {
            string[] conflicting = ["--models", "--prompt", "--history", "--transport", "--max-calls", "--max-output", "--budget-usd"];
            if (conflicting.Any(options.ContainsKey))
                throw new ArgumentException("--configurations cannot be combined with individual configuration flags.");
            var supplied = JsonSerializer.Deserialize<RunConfiguration[]>(json,
                new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 });
            if (supplied is null || supplied.Length is < 1 or > 16 || supplied.Any(c => c is null))
                throw new ArgumentException("--configurations must contain 1–16 RunConfiguration objects.");
            if (supplied.Any(c => c.Mode != "live"))
                throw new ArgumentException("Every configuration must select LIVE.");
            return supplied;
        }
        decimal? budget = null;
        if (options.TryGetValue("--budget-usd", out var raw))
        {
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                throw new ArgumentException("--budget-usd must be an explicit positive decimal.");
            budget = parsed;
        }
        var models = Csv(options.GetValueOrDefault("--models", "gpt5"));
        foreach (var model in models)
            if (!ModelCatalog.Defaults.Any(m => m.Id == model)) throw new ArgumentException($"Unknown model profile: {model}.");
        return models.Select(model => new RunConfiguration
        {
            Mode = "live", ModelProfileId = model,
            PromptProfile = options.GetValueOrDefault("--prompt", "good"),
            HistoryStrategy = options.GetValueOrDefault("--history", "full"),
            ToolTransport = options.GetValueOrDefault("--transport", "direct"),
            MaxModelCalls = Integer(options, "--max-calls", 24, 1, 64),
            MaxOutputTokens = Integer(options, "--max-output", 1500, 1, 16384),
            ApprovedBudgetUsd = budget
        }).ToArray();
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (Flags.Contains(key))
            {
                if (!options.TryAdd(key, "true")) throw new ArgumentException($"Duplicate option: {key}.");
            }
            else if (Options.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                if (!options.TryAdd(key, args[++i])) throw new ArgumentException($"Duplicate option: {key}.");
            }
            else throw new ArgumentException($"Unknown or incomplete option: {key}.");
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"{key} is required.");

    private static int Integer(Dictionary<string, string> options, string key, int fallback, int min, int max) =>
        !options.TryGetValue(key, out var raw) ? fallback
        : int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
            ? parsed : throw new ArgumentException($"{key} must be between {min} and {max}.");

    private static string[] Csv(string value)
    {
        var result = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (result.Length == 0 || result.Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new ArgumentException("Lists must be nonempty and contain distinct values.");
        return result;
    }

    private static async Task<string> Save(string directory, string name, object value, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false), token);
        return file;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record AcceptedTurn(string RunId, string ConversationId, string EventsUrl);
}
