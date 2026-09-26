using System.ComponentModel;
using Microsoft.Extensions.AI;
using Observatory.Core;

namespace Observatory.Agents;

public sealed class ShopFunctions(IShopOperations shop, RunState state)
{
    public IList<AITool> Catalog() =>
        [
            AIFunctionFactory.Create(SearchProducts, "search_products"),
            AIFunctionFactory.Create(QueryCatalog, "query_catalog"),
            AIFunctionFactory.Create(GetCatalogFacets, "get_catalog_facets"),
            AIFunctionFactory.Create(GetProduct, "get_product")
        ];

    public IList<AITool> Orders() =>
        [
            AIFunctionFactory.Create(GetOrder, "get_order"),
            AIFunctionFactory.Create(CreateReturnDraft, "create_return_draft")
        ];

    public IList<AITool> Returns() =>
        [
            AIFunctionFactory.Create(AssessReturn, "assess_return"),
            AIFunctionFactory.Create(GetPolicies, "get_policies")
        ];

    [Description("Cerca prodotti pubblici senza immagini. query accetta termini italiani o inglesi; maxPrice e in USD. Restituisce solo una lista limitata: per contare o combinare categoria e colore usa query_catalog.")]
    private Task<IReadOnlyList<ProductFact>> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(AgentNames.Catalog, "search_products",
            () => shop.SearchProductsAsync(query, maxPrice, take, cancellationToken));

    [Description("Filtra e conta l'intero catalogo. totalProducts conta i modelli distinti; stockUnits somma i pezzi; inStockProducts conta i modelli disponibili. take (1-30) limita solo gli esempi, mai i totali. Categoria e colore accettano italiano o inglese. I colori provengono dal testo, non dalle immagini; mantieni i filtri pertinenti nei follow-up.")]
    private Task<CatalogQueryResponse> QueryCatalog(
        [Description("Parole chiave del prodotto o della marca; non l'intera domanda.")] string? query = null,
        [Description("Categoria del catalogo o gruppo: clothing/abbigliamento/vestiti, dresses/abiti, shirts/camicie, shoes/scarpe, bags/borse, sunglasses/occhiali, jewellery/gioielli.")] string? category = null,
        [Description("Un colore, ad esempio rosso/red, nero/black, blu/blue; null per tutti i colori.")] string? color = null,
        [Description("Prezzo massimo inclusivo in USD; null per nessun limite.")] decimal? maxPrice = null,
        [Description("true esclude i prodotti con stock zero.")] bool inStockOnly = false,
        int take = 5, CancellationToken cancellationToken = default) =>
        InvokeAsync(AgentNames.Catalog, "query_catalog", () => shop.QueryCatalogAsync(new()
        {
            Query = query, Category = category, Color = color, MaxPrice = maxPrice, InStockOnly = inStockOnly, Take = take
        }, cancellationToken));

    [Description("Elenca categorie e colori testuali realmente presenti nel catalogo, con numero di modelli e pezzi. Utile per orientare il cliente. I conteggi dei colori si sovrappongono per prodotti multicolore.")]
    private Task<CatalogFacetsResponse> GetCatalogFacets(CancellationToken cancellationToken) =>
        InvokeAsync(AgentNames.Catalog, "get_catalog_facets", () => shop.GetCatalogFacetsAsync(cancellationToken));

    [Description("Leggi i dati pubblici verificati di un prodotto tramite il suo ID. Non include immagini o miniature.")]
    private Task<ProductFact> GetProduct(int productId, CancellationToken cancellationToken) =>
        InvokeAsync(AgentNames.Catalog, "get_product", () => shop.GetProductAsync(productId, cancellationToken));

    [Description("Leggi un ordine del cliente autenticato della run; distingue l'importo effettivamente pagato dal prezzo di listino.")]
    private Task<ShopOrder> GetOrder(string orderId, CancellationToken cancellationToken) =>
        InvokeAsync(AgentNames.Orders, "get_order", () => shop.GetOrderAsync(orderId, state.Request.CustomerId, cancellationToken));

    [Description("Valuta il motivo corrente del reso secondo ordine e policy effettivi; reason deve riflettere l'ultima correzione del cliente.")]
    private Task<ReturnAssessment> AssessReturn(string orderId, string reason, CancellationToken cancellationToken) =>
        InvokeAsync(AgentNames.Returns, "assess_return",
            () => shop.AssessReturnAsync(orderId, reason, state.Request.CustomerId, cancellationToken));

    [Description("Leggi le policy sintetiche del negozio in ordine di priorita.")]
    private Task<IReadOnlyList<ShopPolicy>> GetPolicies(CancellationToken cancellationToken) =>
        InvokeAsync(AgentNames.Returns, "get_policies", () => shop.GetPoliciesAsync(cancellationToken));

    [Description("Crea una bozza sintetica di reso, non un rimborso. L'autorizzazione proviene solo dalla configurazione server della run, mai da argomenti del modello. L'idoneita viene ricontrollata.")]
    private async Task<ReturnDraft> CreateReturnDraft(string orderId, string reason, CancellationToken cancellationToken)
    {
        if (!state.Request.Configuration.ConfirmAction)
            throw new DomainException("confirmation_required", "Bozza non creata: occorre Configuration.ConfirmAction=true.");
        var assessment = await AssessReturn(orderId, reason, cancellationToken).ConfigureAwait(false);
        if (!assessment.Eligible || assessment.NeedsClarification)
            throw new DomainException("return_not_allowed", "Bozza non creata: il reso non è autorizzato dalla policy applicabile.");
        return await InvokeAsync(AgentNames.Orders, "create_return_draft",
            () => shop.CreateReturnDraftAsync(orderId, reason, true, state.Request.CustomerId, cancellationToken)).ConfigureAwait(false);
    }

    private async Task<T> InvokeAsync<T>(string service, string operation, Func<Task<T>> invoke)
    {
        var endpoint = shop.ServiceEndpoint(service);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = null;
        if (endpoint is not null)
            await state.EmitAsync("protocol.request", AgentNames.Router, $"HTTP {service}: {operation}.", new
            {
                protocol = "HTTP", service, operation, url = endpoint.AbsoluteUri
            }).ConfigureAwait(false);
        try
        {
            var result = await invoke().ConfigureAwait(false);
            state.ObserveDomain(result);
            return result;
        }
        catch (Exception error)
        {
            failure = error;
            if (error is not DomainException) state.Fail(error);
            throw;
        }
        finally
        {
            if (endpoint is not null)
                await state.EmitAsync("protocol.response", AgentNames.Router, $"HTTP {service}: {operation}.", new
                {
                    protocol = "HTTP", service, operation, url = endpoint.AbsoluteUri,
                    durationMs = watch.Elapsed.TotalMilliseconds,
                    status = failure is OperationCanceledException ? "cancelled" : failure is null ? "completed" : "failed",
                    error = failure is null ? null : SafeTelemetry.Text(failure.Message)
                }).ConfigureAwait(false);
        }
    }
}
