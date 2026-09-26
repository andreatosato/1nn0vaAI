using Microsoft.Extensions.AI;
using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Catalog.Api;

public sealed class CatalogAgent(AgentSession session, IShopData catalog) : ISpecialistAgent
{
    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        session.RunAsync(AgentNames.Catalog, request,
            AgentPrompts.History(request).Append(new ChatMessage(ChatRole.User, query)),
            state => new ShopFunctions(new LocalShopOperations(catalog), state).Catalog(),
            emit, cancellationToken);
}
