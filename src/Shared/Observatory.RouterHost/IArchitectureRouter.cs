using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

/// <summary>What the UI needs to know about one architecture: technology, agents with a model, and topology.</summary>
public sealed record DemoArchitecture(string Technology, IReadOnlyList<string> Agents, string Topology);

/// <summary>
/// The root agent of one architecture (Inline, Skills or A2A). Each router project provides exactly one.
/// </summary>
public interface IArchitectureRouter : IAgentRuntime, IAgent
{
    DemoArchitecture Architecture { get; }

    /// <summary>Exact instructions of every agent with a model in this architecture, without running anything.</summary>
    Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken);
}
