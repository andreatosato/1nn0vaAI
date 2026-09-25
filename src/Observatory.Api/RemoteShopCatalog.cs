using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

public sealed class RemoteShopCatalog(ShopServiceClient client) : IShopCatalog
{
    private CatalogSnapshot? _catalog;

    public CatalogSnapshot Catalog => _catalog ?? throw new InvalidOperationException("Il catalogo remoto non e stato inizializzato.");
    public IReadOnlyList<Product> Products => Catalog.Products;
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; } = ScenarioCatalog.Create();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var snapshot = await client.GetCatalogAsync(cancellationToken);
        if (snapshot.Products is null || snapshot.Products.Count == 0 || snapshot.Products.Any(product => product is null)
            || string.IsNullOrWhiteSpace(snapshot.ContentHash))
            throw new InvalidDataException("Il servizio Catalog ha restituito uno snapshot privo di prodotti o provenance.");
        _catalog = snapshot;
    }
}
