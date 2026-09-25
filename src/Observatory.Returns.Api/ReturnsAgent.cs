using Microsoft.Extensions.AI;
using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Returns.Api;

public sealed class ReturnsAgent(AgentSession session, IShopData returns) : ISpecialistAgent
{
    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        session.RunAsync(AgentNames.Returns, request,
            AgentPrompts.History(request).Append(new ChatMessage(ChatRole.User, query)),
            state => new ShopFunctions(new LocalShopOperations(returns), state).Returns(),
            emit, cancellationToken);
}
