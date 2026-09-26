using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.SpecialistHost;

/// <summary>One A2A specialist agent. Each specialist project provides exactly one implementation.</summary>
public interface ISpecialistAgent : IAgent
{
    /// <summary>Agent name: catalog, orders or returns.</summary>
    string Role { get; }

    /// <summary>Text of the A2A agent card and of its single skill.</summary>
    string CardDescription { get; }
    string SkillDescription { get; }

    Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken);
}
