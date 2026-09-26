using Microsoft.Extensions.AI;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Router.Api;

public sealed class RouterAgent(AgentSession session, A2ATransport transport) : IAgentRuntime
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.A2A)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto il router A2A.");
        var result = await session.RunAsync(AgentNames.Router, request, AgentPrompts.History(request),
            state =>
            [
                DelegateTo(state, AgentNames.Catalog,
                    "Delega al servizio Catalog tramite A2A: ricerca, dettagli, conteggi completi di modelli e pezzi, categorie e colori testuali. Includi i filtri correnti e il contesto utile del follow-up."),
                DelegateTo(state, AgentNames.Orders,
                    "Delega allo specialista orders tramite A2A ufficiale su HTTP. Restituisce solo fatti di dominio verificati."),
                DelegateTo(state, AgentNames.Returns,
                    "Delega allo specialista returns tramite A2A ufficiale su HTTP. Restituisce solo fatti di dominio verificati.")
            ], emit, cancellationToken);
        await emit(new RunEvent
        {
            RunId = request.RunId, Kind = "answer.delta", Agent = AgentNames.Router,
            Message = result.Answer, Data = new { text = result.Answer, buffered = true }
        });
        return result;
    }

    private AITool DelegateTo(RunState state, string role, string description) =>
        AIFunctionFactory.Create(async (string query, CancellationToken token) =>
        {
            try { return await transport.InvokeAsync(state, role, query, token); }
            catch (Exception error) { state.Fail(error); throw; }
        }, $"{role}_agent", description);
}
