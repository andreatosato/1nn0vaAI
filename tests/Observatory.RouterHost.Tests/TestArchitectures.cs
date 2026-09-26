using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

/// <summary>The three architectures as declared by the router projects, plus a probe router for host tests.</summary>
internal static class TestArchitectures
{
    public static readonly DemoArchitecture Inline = new(DemoTechnologies.Inline, [AgentNames.Router], "router-http");
    public static readonly DemoArchitecture Skills = new(DemoTechnologies.Skills, [AgentNames.Router], "router-skills-http");
    public static readonly DemoArchitecture A2A = new(DemoTechnologies.A2A, AgentNames.All, "router-a2a");

    public static DemoArchitecture For(string technology) => technology switch
    {
        DemoTechnologies.Inline => Inline,
        DemoTechnologies.Skills => Skills,
        DemoTechnologies.A2A => A2A,
        _ => throw new ArgumentOutOfRangeException(nameof(technology))
    };

    /// <summary>Instructions of the probe agents: shared rules + a placeholder procedure + blocks.</summary>
    public static string Instructions(string agent, AgentRunRequest request) =>
        AgentPrompts.Instructions(agent, request, $"Procedura di prova per {agent}.");

    public static PromptPreviewResponse Preview(string technology, RunConfiguration configuration)
    {
        var request = PromptLaboratory.PreviewRequest(technology, configuration);
        return PromptLaboratory.Preview(technology,
            For(technology).Agents.Select(agent => PromptLaboratory.For(agent, Instructions(agent, request))).ToArray());
    }
}

/// <summary>Router used by host tests: runs through a probe runtime, previews the probe instructions.</summary>
internal sealed class ProbeRouter(DemoArchitecture architecture, IAgentRuntime runtime) : IArchitectureRouter
{
    public DemoArchitecture Architecture => architecture;

    public string Instructions(AgentRunRequest request) => TestArchitectures.Instructions(AgentNames.Router, request);

    public IList<AITool> Tools(RunState state) => [];

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default) => runtime.ExecuteAsync(request, emit, cancellationToken);

    public Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult(TestArchitectures.Preview(architecture.Technology, configuration).Agents);
}
