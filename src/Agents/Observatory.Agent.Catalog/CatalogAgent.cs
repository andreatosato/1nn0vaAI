using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Agent.Catalog.Tools;
using Observatory.SpecialistHost;

namespace Observatory.Agent.Catalog;

public sealed class CatalogAgent(AgentRunner runner, ShopServiceClient shop) : ISpecialistAgent
{
    public string Role => AgentNames.Catalog;

    public string CardDescription =>
        "Specialista catalog del negozio, realizzato con ChatClientAgent di Microsoft Agent Framework. Restituisce solo dati di dominio verificati.";

    public string SkillDescription =>
        "Ricerca prodotti, conteggia modelli e pezzi su filtri combinati, leggi dettagli, categorie e colori testuali del catalogo.";

    public string Instructions(AgentRunRequest request) => AgentPrompts.Instructions(Role, request, Procedure);

    public IList<AITool> Tools(RunState state) => new CatalogTools(shop, state, Role).All();

    public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
        Func<RunEvent, Task> emit, CancellationToken cancellationToken) =>
        runner.RunAsync(
            new AgentDefinition(Role, Instructions, Tools),
            request, [.. AgentPrompts.History(request), new ChatMessage(ChatRole.User, query)], emit, cancellationToken);

    // Published verbatim as the 'catalog' skill by src/Skills/Observatory.Skill.Catalog (checked by a test).
    public const string Procedure = """
        Per ricerche con filtri o conteggi usa query_catalog(query, category, color, maxPrice, inStockOnly, take).
        query contiene parole chiave del prodotto o della marca, non tutta la domanda. Mantieni i filtri pertinenti
        dei turni precedenti: per esempio, "solo rosse" cambia color ma non cancella categoria, budget o disponibilità.
        Per "quanti prodotti disponibili" usa inStockOnly=true; se si chiedono i pezzi, riferisci stockUnits.
        totalProducts, inStockProducts e stockUnits sono calcolati prima di take; products contiene solo gli esempi
        restituiti. hasMore segnala altri risultati: non presentare la pagina come catalogo completo.
        Usa get_catalog_facets per scoprire categorie e colori realmente presenti, non per indovinare conteggi
        di filtri combinati. I valori productCount e stockUnits delle categorie o dei colori sono distinti;
        i gruppi di colore possono sovrapporsi e non vanno sommati. Spiega colorBasis quando il colore è rilevante.
        category accetta ID reali e gruppi clothing, dresses, shirts, shoes, bags, sunglasses, jewellery, anche con alias italiani.
        Se "vestiti" indica genericamente cosa acquistare, considera clothing e chiarisci brevemente che intendi abbigliamento.
        Se serve un tipo preciso di abito e il contesto non lo identifica, chiedi quale: non scegliere un sottotipo a caso.
        search_products resta disponibile per semplici elenchi, ma non prova il totale. Usa get_product per un ID pubblico esplicito.
        Riferisci ID, titolo, prezzo, valuta e scorte soltanto come restituiti. Il colore testuale non certifica varianti viste in foto.
        Non allargare silenziosamente i filtri se non trovi risultati. GET /catalog è solo per metadata/UI, non è un tool del modello.
        """;
}
