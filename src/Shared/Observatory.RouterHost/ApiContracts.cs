using System.Text.Json;
using System.Text.Json.Serialization;
using Observatory.Core;

namespace Observatory.Api;

public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions Requests = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Stored JSON is null.");
}

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record CreateConversationRequest(string? Title = null);
public sealed record TurnAccepted(string RunId, string ConversationId, string EventsUrl);
public sealed record SubmissionResult(RunRecord Run, bool Duplicate);

public sealed record CatalogProvenance(string Source, string SourceUrl, DateTimeOffset RetrievedAt,
    string ContentHash, int ProductCount, string Notice, string ImagesPolicy);

public sealed record RunSnapshot
{
    public string SchemaVersion { get; init; } = "1";
    public required SubmitTurnRequest Request { get; init; }
    public required string ExactRequestBody { get; init; }
    public required string Technology { get; init; }
    public required ModelDefinition[] Models { get; init; }
    public required CatalogProvenance Catalog { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CustomerId { get; init; } = DemoClock.CustomerId;
    public string ApplicationVersion { get; init; } =
        typeof(RunSnapshot).Assembly.GetName().Version?.ToString() ?? "unknown";
}

public sealed record ExperimentCaseResult(string ScenarioId, int ConfigurationIndex, int Repetition,
    string ConversationId, string Status, IReadOnlyList<string> RunIds,
    IReadOnlyList<EvaluationResult> Evaluations, IReadOnlyList<string> Failures);

public sealed record ExperimentResult(string Id, string Status, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, IReadOnlyList<ExperimentCaseResult> Cases, decimal? EstimatedCostUsd,
    string CostStatus, string Notice);
