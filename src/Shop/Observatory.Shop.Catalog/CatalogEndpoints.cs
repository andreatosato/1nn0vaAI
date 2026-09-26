using Observatory.Core;

namespace Observatory.Shop.Catalog;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("").WithTags("catalog")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        api.MapGet("/catalog", (IShopData shop) => TypedResults.Ok(shop.Catalog))
            .WithName("CatalogSnapshot")
            .WithSummary("Catalog snapshot for UI and provenance; never a model tool.");

        api.MapGet("/products", (string? query, decimal? maxPrice, int? take, IShopData shop, CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return TypedResults.Ok(shop.SearchProducts(query, maxPrice, take ?? 5));
        }).WithName("SearchProducts").WithSummary("Search public image-free product facts.");

        api.MapGet("/catalog/query", (string? query, string? category, string? color, decimal? maxPrice,
            bool? inStockOnly, int? take, IShopData shop, CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return TypedResults.Ok(shop.QueryCatalog(new()
            {
                Query = query, Category = category, Color = color, MaxPrice = maxPrice,
                InStockOnly = inStockOnly ?? false, Take = take ?? 5
            }));
        }).WithName("QueryCatalog")
            .WithSummary("Filtra il catalogo e conta modelli e pezzi senza limitare i totali alla pagina.")
            .WithDescription("Categoria, colore testuale e prezzo in USD si combinano con AND. take 1-30 limita solo gli esempi; nessuna immagine viene restituita.");

        api.MapGet("/catalog/facets", (IShopData shop, CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return TypedResults.Ok(shop.GetCatalogFacets());
        }).WithName("CatalogFacets")
            .WithSummary("Scopri categorie e colori presenti, con conteggi e provenienza testuale dei colori.");

        api.MapGet("/products/{productId:int}", (int productId, IShopData shop) => TypedResults.Ok(shop.GetProduct(productId)))
            .WithName("GetProduct").WithSummary("Read one public image-free product fact.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
