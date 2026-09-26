using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Observatory.ServiceDefaults;

/// <summary>
/// Native Microsoft.Extensions.AI and Agent Framework OpenTelemetry, plus the one thing the GenAI
/// conventions do not carry: the estimated cost of each model call.
/// </summary>
public static class AiTelemetryExtensions
{
    public const string ChatSourceName = "Experimental.Microsoft.Extensions.AI";
    public const string AgentSourceName = "Experimental.Microsoft.Agents.AI";
    public const string CostMeterName = "Observatory.AI";

    private static readonly Meter CostMeter = new(CostMeterName);
    private static readonly Counter<double> Cost = CostMeter.CreateCounter<double>(
        "observatory.ai.cost", "USD", "Estimated model cost from provider usage and the configured rate card.");

    public static ChatClientBuilder UseObservatoryTelemetry(this ChatClientBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: ChatSourceName,
            configure: telemetry => telemetry.EnableSensitiveData = false);

    public static AIAgentBuilder UseObservatoryTelemetry(this AIAgentBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: AgentSourceName,
            configure: telemetry => telemetry.EnableSensitiveData = false);

    /// <summary>Adds cache and cost facts to the current native chat span and to the cost counter.</summary>
    public static void RecordModelCall(string agent, string modelProfile, long? cachedInputTokens, decimal? costUsd)
    {
        var span = Activity.Current;
        span?.SetTag("observatory.agent", agent);
        span?.SetTag("observatory.model_profile", modelProfile);
        if (cachedInputTokens is { } cached) span?.SetTag("gen_ai.usage.cache_read.input_tokens", cached);
        if (costUsd is not { } cost) return;
        span?.SetTag("observatory.cost.usd", (double)cost);
        Cost.Add((double)cost, new KeyValuePair<string, object?>("gen_ai.agent.name", agent),
            new KeyValuePair<string, object?>("observatory.model_profile", modelProfile));
    }
}
