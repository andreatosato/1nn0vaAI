using Microsoft.Extensions.AI;
using Observatory.Core;

namespace Observatory.AgentRuntime;

/// <summary>
/// What distinguishes one agent from another: its instructions and its tools.
/// Model pipeline, evidence and telemetry are shared (see <see cref="AgentRunner"/>).
/// </summary>
public interface IAgent
{
    /// <summary>Exact text sent as ChatOptions.Instructions.</summary>
    string Instructions(AgentRunRequest request);

    /// <summary>Tools offered to the model for one run.</summary>
    IList<AITool> Tools(RunState state);
}
