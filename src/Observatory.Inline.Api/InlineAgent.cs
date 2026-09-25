using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Inline.Api;

public sealed class InlineAgent(AgentSession session, ShopServiceClient shop) : IAgentRuntime
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.Inline)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto Inline.");
        var result = await session.RunAsync(AgentNames.Router, request, AgentPrompts.History(request),
            state =>
            {
                var tools = new ShopFunctions(shop, state);
                return [.. tools.Catalog(), .. tools.Orders(), .. tools.Returns()];
            }, emit, cancellationToken);
        await emit(new RunEvent
        {
            RunId = request.RunId, Kind = "answer.delta", Agent = AgentNames.Router,
            Message = result.Answer, Data = new { text = result.Answer, buffered = true }
        });
        return result;
    }
}
