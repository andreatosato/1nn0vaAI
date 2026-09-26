using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Agent.Returns.Tools;
using Observatory.SpecialistHost;

namespace Observatory.Agent.Returns;

public sealed class ReturnsAgent(AgentRunner runner, ShopServiceClient shop) : ISpecialistAgent
{
    public string Role => AgentNames.Returns;

    public string CardDescription =>
        "Specialista returns del negozio, realizzato con ChatClientAgent di Microsoft Agent Framework. Restituisce solo dati di dominio verificati.";

    public string SkillDescription =>
        "Esegue lo specialista returns con configurazione isolata della run, identita cliente vincolata al server e strumenti autorevoli del negozio.";

    public string Instructions(AgentRunRequest request) => AgentPrompts.Instructions(Role, request, Procedure);

    public IList<AITool> Tools(RunState state) => new ReturnsTools(shop, state, Role).All();

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        runner.RunAsync(
            new AgentDefinition(Role, Instructions, Tools),
            request, [.. AgentPrompts.History(request), new ChatMessage(ChatRole.User, query)], emit, cancellationToken);

    // Published verbatim as the 'returns' skill by src/Skills/Observatory.Skill.Returns (checked by a test).
    public const string Procedure = """
        Usa assess_return con l'ID ordine esplicito e il motivo corrente, comprese le ultime correzioni.
        Usa get_policies se serve chiarire il testo. Non ricavare una policy dalla memoria.
        Spiega priorità e ID della policy, tempo dalla consegna, ammissibilità, chiarimenti e importo del rimborso
        esattamente come restituiti. Una valutazione non crea una bozza e non esegue alcun rimborso.
        """;
}
