using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Agent.Orders.Tools;
using Observatory.SpecialistHost;

namespace Observatory.Agent.Orders;

public sealed class OrdersAgent(AgentRunner runner, ShopServiceClient shop) : ISpecialistAgent
{
    public string Role => AgentNames.Orders;

    public string CardDescription =>
        "Specialista orders del negozio, realizzato con ChatClientAgent di Microsoft Agent Framework. Restituisce solo dati di dominio verificati.";

    public string SkillDescription =>
        "Esegue lo specialista orders con configurazione isolata della run, identita cliente vincolata al server e strumenti autorevoli del negozio.";

    public string Instructions(AgentRunRequest request) => AgentPrompts.Instructions(Role, request, Procedure);

    public IList<AITool> Tools(RunState state) => new OrdersTools(shop, state, Role).All();

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        runner.RunAsync(
            new AgentDefinition(Role, Instructions, Tools),
            request, [.. AgentPrompts.History(request), new ChatMessage(ChatRole.User, query)], emit, cancellationToken);

    // Published verbatim as the 'orders' skill by src/Skills/Observatory.Skill.Orders (checked by a test).
    public const string Procedure = """
        Usa get_order con l'ID ordine esplicito e il cliente vincolato dal server.
        Distingui il prezzo di listino dall'importo pagato, conservando valuta e data di consegna.
        Usa create_return_draft solo per una richiesta esplicita di bozza con autorizzazione server confermata.
        Il tool ricontrolla l'ammissibilità: dichiara creata la bozza soltanto dopo un risultato riuscito.
        """;
}
