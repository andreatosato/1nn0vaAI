using System.ComponentModel;
using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Router.Skills.Tools;

/// <summary>Model tools over the Catalog business API (HTTP). Copy of the specialist agent tools: same names, descriptions and parameters.</summary>
public sealed class CatalogTools(IShopOperations shop, RunState state, string agent)
{
    public IList<AITool> All() =>
        [
            AIFunctionFactory.Create(SearchProducts, "search_products"),
            AIFunctionFactory.Create(QueryCatalog, "query_catalog"),
            AIFunctionFactory.Create(GetCatalogFacets, "get_catalog_facets"),
            AIFunctionFactory.Create(GetProduct, "get_product")
        ];

    [Description("Cerca prodotti pubblici senza immagini. query accetta termini italiani o inglesi; maxPrice e in USD. Restituisce solo una lista limitata: per contare o combinare categoria e colore usa query_catalog.")]
    private Task<IReadOnlyList<ProductFact>> SearchProducts(string? query = null, decimal? maxPrice = null, int take = 5,
        CancellationToken cancellationToken = default) =>
        Call("search_products", () => shop.SearchProductsAsync(query, maxPrice, take, cancellationToken));

    [Description("Filtra e conta l'intero catalogo. totalProducts conta i modelli distinti; stockUnits somma i pezzi; inStockProducts conta i modelli disponibili. take (1-30) limita solo gli esempi, mai i totali. Categoria e colore accettano italiano o inglese. I colori provengono dal testo, non dalle immagini; mantieni i filtri pertinenti nei follow-up.")]
    private Task<CatalogQueryResponse> QueryCatalog(
        [Description("Parole chiave del prodotto o della marca; non l'intera domanda.")] string? query = null,
        [Description("Categoria del catalogo o gruppo: clothing/abbigliamento/vestiti, dresses/abiti, shirts/camicie, shoes/scarpe, bags/borse, sunglasses/occhiali, jewellery/gioielli.")] string? category = null,
        [Description("Un colore, ad esempio rosso/red, nero/black, blu/blue; null per tutti i colori.")] string? color = null,
        [Description("Prezzo massimo inclusivo in USD; null per nessun limite.")] decimal? maxPrice = null,
        [Description("true esclude i prodotti con stock zero.")] bool inStockOnly = false,
        int take = 5, CancellationToken cancellationToken = default) =>
        Call("query_catalog", () => shop.QueryCatalogAsync(new()
        {
            Query = query, Category = category, Color = color, MaxPrice = maxPrice, InStockOnly = inStockOnly, Take = take
        }, cancellationToken));

    [Description("Elenca categorie e colori testuali realmente presenti nel catalogo, con numero di modelli e pezzi. Utile per orientare il cliente. I conteggi dei colori si sovrappongono per prodotti multicolore.")]
    private Task<CatalogFacetsResponse> GetCatalogFacets(CancellationToken cancellationToken) =>
        Call("get_catalog_facets", () => shop.GetCatalogFacetsAsync(cancellationToken));

    [Description("Leggi i dati pubblici verificati di un prodotto tramite il suo ID. Non include immagini o miniature.")]
    private Task<ProductFact> GetProduct(int productId, CancellationToken cancellationToken) =>
        Call("get_product", () => shop.GetProductAsync(productId, cancellationToken));

    private Task<T> Call<T>(string operation, Func<Task<T>> invoke) =>
        ShopToolCall.InvokeAsync(shop, state, agent, AgentNames.Catalog, operation, invoke);
}
