using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Inline;
using Observatory.RouterHost;

namespace Observatory.Router.Inline;

/// <summary>Hosts the <see cref="InlineAgent"/> (src/Inline): the prompt and the tools live in that library.</summary>
public sealed class InlineRouter(AgentRunner runner, ShopServiceClient shop) : IArchitectureRouter
{
    public static readonly DemoArchitecture Architecture = new(DemoTechnologies.Inline, [AgentNames.Router], "router-http");

    private readonly InlineAgent _agent = new(shop);

    DemoArchitecture IArchitectureRouter.Architecture => Architecture;

    public string Instructions(AgentRunRequest request) => _agent.Instructions(request);

    public IList<AITool> Tools(RunState state) => _agent.Tools(state);

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.Inline)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto il router Inline.");
        return runner.RunAsync(_agent.Definition(), request, AgentPrompts.History(request), emit, cancellationToken);
    }

    public Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentPromptPreview>>(
            [PromptLaboratory.For(AgentNames.Router, Instructions(PromptLaboratory.PreviewRequest(Architecture.Technology, configuration)))]);
}
