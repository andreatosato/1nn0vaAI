using Observatory.Core;

namespace Observatory.Agents;

public interface IShopOperations
{
    Uri? ServiceEndpoint(string role);
    Task<IReadOnlyList<ProductFact>> SearchProductsAsync(string? query, decimal? maxPrice, int take, CancellationToken token);
    Task<CatalogQueryResponse> QueryCatalogAsync(CatalogQueryRequest query, CancellationToken token);
    Task<CatalogFacetsResponse> GetCatalogFacetsAsync(CancellationToken token);
    Task<ProductFact> GetProductAsync(int productId, CancellationToken token);
    Task<ShopOrder> GetOrderAsync(string orderId, string customerId, CancellationToken token);
    Task<ReturnAssessment> AssessReturnAsync(string orderId, string reason, string customerId, CancellationToken token);
    Task<IReadOnlyList<ShopPolicy>> GetPoliciesAsync(CancellationToken token);
    Task<ReturnDraft> CreateReturnDraftAsync(string orderId, string reason, bool confirmed, string customerId, CancellationToken token);
}

public sealed class LocalShopOperations(IShopData shop) : IShopOperations
{
    public Uri? ServiceEndpoint(string role) => null;

    public Task<IReadOnlyList<ProductFact>> SearchProductsAsync(string? query, decimal? maxPrice, int take, CancellationToken token) =>
        Execute(() => shop.SearchProducts(query, maxPrice, take), token);

    public Task<CatalogQueryResponse> QueryCatalogAsync(CatalogQueryRequest query, CancellationToken token) =>
        Execute(() => shop.QueryCatalog(query), token);

    public Task<CatalogFacetsResponse> GetCatalogFacetsAsync(CancellationToken token) =>
        Execute(shop.GetCatalogFacets, token);

    public Task<ProductFact> GetProductAsync(int productId, CancellationToken token) =>
        Execute(() => shop.GetProduct(productId), token);

    public Task<ShopOrder> GetOrderAsync(string orderId, string customerId, CancellationToken token) =>
        Execute(() => shop.GetOrder(orderId, customerId), token);

    public Task<ReturnAssessment> AssessReturnAsync(string orderId, string reason, string customerId, CancellationToken token) =>
        Execute(() => shop.AssessReturn(orderId, reason, customerId), token);

    public Task<IReadOnlyList<ShopPolicy>> GetPoliciesAsync(CancellationToken token) =>
        Execute<IReadOnlyList<ShopPolicy>>(() => shop.Policies.OrderByDescending(policy => policy.Priority).ToArray(), token);

    public Task<ReturnDraft> CreateReturnDraftAsync(string orderId, string reason, bool confirmed, string customerId, CancellationToken token) =>
        Execute(() => shop.CreateReturnDraft(orderId, reason, confirmed, customerId), token);

    private static Task<T> Execute<T>(Func<T> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(operation());
    }
}

public sealed record ReturnOperationRequest(string OrderId, string Reason);

public sealed class ShopServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
