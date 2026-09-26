using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.RouterHost;

namespace Observatory.Router.A2A;

public sealed class A2ARouter(AgentRunner runner, SpecialistClient specialists) : IArchitectureRouter
{
    public static readonly DemoArchitecture Architecture = new(DemoTechnologies.A2A, AgentNames.All, "router-a2a");

    DemoArchitecture IArchitectureRouter.Architecture => Architecture;

    public string Instructions(AgentRunRequest request) => AgentPrompts.Instructions(AgentNames.Router, request, Procedure);

    // Agent-as-tool: one tool per remote specialist; control returns to the router after each answer.
    public IList<AITool> Tools(RunState state) =>
    [
        DelegateTo(state, AgentNames.Catalog,
            "Delega al servizio Catalog tramite A2A: ricerca, dettagli, conteggi completi di modelli e pezzi, categorie e colori testuali. Includi i filtri correnti e il contesto utile del follow-up."),
        DelegateTo(state, AgentNames.Orders,
            "Delega allo specialista orders tramite A2A ufficiale su HTTP. Restituisce solo fatti di dominio verificati."),
        DelegateTo(state, AgentNames.Returns,
            "Delega allo specialista returns tramite A2A ufficiale su HTTP. Restituisce solo fatti di dominio verificati.")
    ];

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.A2A)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto il router A2A.");
        var router = new AgentDefinition(AgentNames.Router, Instructions, Tools) { PublishesAnswer = true };
        return runner.RunAsync(router, request, AgentPrompts.History(request), emit, cancellationToken);
    }

    // The router owns only its own instructions; each specialist reports its own.
    public async Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken)
    {
        var previews = new List<AgentPromptPreview>
        {
            PromptLaboratory.For(AgentNames.Router, Instructions(PromptLaboratory.PreviewRequest(Architecture.Technology, configuration)))
        };
        foreach (var role in AgentNames.Specialists)
            previews.Add(await specialists.PreviewAsync(role, configuration, cancellationToken));
        return previews;
    }

    private AITool DelegateTo(RunState state, string role, string description) =>
        AIFunctionFactory.Create(async (string query, CancellationToken token) =>
        {
            try { return await specialists.InvokeAsync(state, role, query, token); }
            catch (Exception error) { state.Fail(error); throw; }
        }, $"{role}_agent", description);

    private const string Procedure = """
        Delega ricerche, prodotti, colori, scorte e conteggi a catalog_agent; ordini, importi pagati e stato a orders_agent;
        policy e ammissibilità a returns_agent. Sono agenti invocati come tool: il controllo torna a te, non è un passaggio di consegne.
        Invia allo specialista la domanda corrente con i filtri pertinenti già chiariti nella conversazione, senza inventarne altri.
        Non contare gli esempi restituiti come totale: conserva la distinzione fra prodotti, pezzi e pagina dei risultati.
        Per i resi identifica prima l'ordine, poi valuta il motivo più recente. Una correzione esplicita sostituisce il motivo precedente.
        Chiedi una bozza a orders_agent solo per una richiesta esplicita, dopo una valutazione ammissibile e con conferma server abilitata.
        Ricomponi le risposte degli specialisti senza perdere ID prodotto, prezzi, valuta, policy o condizioni rilevanti.
        """;
}
