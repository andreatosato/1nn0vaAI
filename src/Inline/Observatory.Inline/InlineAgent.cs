using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Inline.Tools;

namespace Observatory.Inline;

/// <summary>
/// The Inline architecture: one agent that owns every tool and has every integration rule written in its prompt.
/// The router process only hosts it.
/// </summary>
public sealed class InlineAgent(IShopOperations shop)
{
    public string Instructions(AgentRunRequest request) =>
        AgentPrompts.Instructions(AgentNames.Router, request, RouterProcedure + "\n" + CatalogProcedure);

    // The same HTTP tools as the specialists, all on one agent.
    public IList<AITool> Tools(RunState state) =>
        [
            .. new CatalogTools(shop, state, AgentNames.Router).All(),
            .. new OrdersTools(shop, state, AgentNames.Router).All(),
            .. new ReturnsTools(shop, state, AgentNames.Router).All()
        ];

    public AgentDefinition Definition() => new(AgentNames.Router, Instructions, Tools) { PublishesAnswer = true };

    private const string RouterProcedure = """
        Sei l'unico agente con un modello. I tuoi tool di dominio chiamano tre servizi HTTP esterni, non altri agenti.
        Orders: usa get_order con l'ID ordine esplicito e il cliente vincolato dal server; distingui pagato e listino.
        Returns: usa assess_return con l'ID ordine esplicito e l'ultimo motivo; get_policies per spiegare il testo delle policy.
        Per i resi identifica prima l'ordine e poi valuta l'ammissibilità. Una correzione sostituisce il motivo precedente.
        Usa create_return_draft solo per una richiesta esplicita di bozza, con valutazione ammissibile e conferma server.
        Orders ricontrolla ammissibilità e consenso; non esegue pagamenti o rimborsi.
        Ricomponi i fatti verificati senza perdere ID prodotto, prezzi, valuta, policy o condizioni rilevanti.
        """;

    // Same catalog procedure as the Catalog specialist agent: Inline writes every integration rule in the prompt.
    private const string CatalogProcedure = """
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
