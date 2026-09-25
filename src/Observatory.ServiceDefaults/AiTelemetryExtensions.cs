using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Observatory.ServiceDefaults;

public static class AiTelemetryExtensions
{
    public const string ChatSourceName = "Experimental.Microsoft.Extensions.AI";
    public const string AgentSourceName = "Experimental.Microsoft.Agents.AI";

    public static ChatClientBuilder UseObservatoryTelemetry(this ChatClientBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: ChatSourceName,
            configure: telemetry => telemetry.EnableSensitiveData = false);

    public static AIAgentBuilder UseObservatoryTelemetry(this AIAgentBuilder builder) =>
        builder.UseOpenTelemetry(sourceName: AgentSourceName,
            configure: telemetry => telemetry.EnableSensitiveData = false);
}
