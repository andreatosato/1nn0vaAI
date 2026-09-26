using Microsoft.Extensions.AI;
using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Orders.Api;

public sealed class OrdersAgent(AgentSession session, IShopData orders) : ISpecialistAgent
{
    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        session.RunAsync(AgentNames.Orders, request,
            AgentPrompts.History(request).Append(new ChatMessage(ChatRole.User, query)),
            state => new ShopFunctions(new LocalShopOperations(orders), state).Orders(),
            emit, cancellationToken);
}
