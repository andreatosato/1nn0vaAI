using Observatory.Core;

namespace Observatory.AgentRuntime;

/// <summary>The shop business operations the agent tools can call.</summary>
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

public sealed class ShopServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
