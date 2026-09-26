using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Router.Skills.RemoteSkills;
using Observatory.Router.Skills.Tools;
using Observatory.RouterHost;

namespace Observatory.Router.Skills;

public sealed class SkillsRouter(AgentRunner runner, ShopServiceClient shop, IHttpClientFactory http) : IArchitectureRouter
{
    public static readonly DemoArchitecture Architecture = new(DemoTechnologies.Skills, [AgentNames.Router], "router-skills-http");

    DemoArchitecture IArchitectureRouter.Architecture => Architecture;

    public string Instructions(AgentRunRequest request) => AgentPrompts.Instructions(AgentNames.Router, request, Procedure);

    // The same HTTP tools as the three specialists, all on the one agent with a model.
    public IList<AITool> Tools(RunState state) =>
    [
        .. new CatalogTools(shop, state, AgentNames.Router).All(),
        .. new OrdersTools(shop, state, AgentNames.Router).All(),
        .. new ReturnsTools(shop, state, AgentNames.Router).All()
    ];

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.Skills)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto il router Skills.");
        var router = new AgentDefinition(AgentNames.Router, Instructions, Tools) { Context = LoadSkills, PublishesAnswer = true };
        return runner.RunAsync(router, request, AgentPrompts.History(request), emit, cancellationToken);
    }

    public Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentPromptPreview>>(
            [PromptLaboratory.For(AgentNames.Router, Instructions(PromptLaboratory.PreviewRequest(Architecture.Technology, configuration)))]);

    // Native Agent Skills provider fed by the remote skill sites; scripts are never offered.
    private AIContextProvider LoadSkills(RunState state) =>
        new AgentSkillsProviderBuilder()
            .UseSource(new RemoteSkillsSource(http.CreateClient("skills"), state))
            .UseOptions(options =>
            {
                options.DisableLoadSkillApproval = true;
                options.DisableReadSkillResourceApproval = true;
            })
            .Build();

    private const string Procedure = """
        Sei l'unico agente con un modello. Catalog, Orders e Returns sono specialisti remoti: le loro istruzioni sono
        pubblicate come skill catalog, orders e returns da siti dedicati. Non deleghi: carichi la skill e agisci tu.
        Prima di usare i tool di un dominio carica la sua skill; leggi le risorse di riferimento solo quando la skill lo chiede.
        I tool registrati eseguono chiamate HTTP business autenticate e rimangono la fonte autorevole dei fatti.
        Per ricerche, filtri, colori o conteggi carica catalog; conserva i vincoli pertinenti già indicati nella conversazione.
        Per un reso identifica prima l'ordine con orders, poi valuta l'ammissibilità con returns.
        Crea bozze solo su richiesta esplicita e dopo conferma del server.
        Fidati solo dei siti skill-*: solo Markdown, nessuno script e nessun download da altre origini.
        """;
}
