using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Observatory.Core;

namespace Observatory.AgentRuntime;

/// <summary>
/// HTTP client of the three shop business APIs. Logical names such as http://shop-catalog are resolved by Aspire service discovery.
/// </summary>
public sealed class ShopServiceClient(IHttpClientFactory httpClients) : IShopOperations
{
    public const string HttpClientName = "shop";
    private static readonly JsonSerializerOptions ResponseJson = new(AgentJson.Options)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public Uri ServiceEndpoint(string role) => new($"http://shop-{role}/");

    public Task<CatalogSnapshot> GetCatalogAsync(CancellationToken token = default) =>
        SendAsync<CatalogSnapshot>(AgentNames.Catalog, HttpMethod.Get, "catalog", null, null, false, token);

    /// <summary>Fetches the UI-only synthetic teaching snapshot over HTTP; not part of IShopOperations or any agent tool.</summary>
    public async Task<DemoDataResponse> GetDemoDataAsync(CancellationToken token)
    {
        var orders = await SendAsync<ShopOrder[]>(AgentNames.Orders, HttpMethod.Get, "demo-data/orders",
            null, null, false, token).ConfigureAwait(false);
        if (orders.Length == 0 || orders.Any(order => order is null
                || string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.CustomerId)
                || order.ProductId <= 0 || string.IsNullOrWhiteSpace(order.ProductTitle)
                || order.ListPrice < 0 || order.AmountPaid < 0 || string.IsNullOrWhiteSpace(order.Currency)
                || order.DeliveredAt == default || string.IsNullOrWhiteSpace(order.Status)
                || string.IsNullOrWhiteSpace(order.TrackingCode))
            || orders.Select(order => order.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != orders.Length)
            throw new ShopServiceException("Dati ordini demo vuoti o non validi; nessun fallback locale.");

        var policies = await SendAsync<ShopPolicy[]>(AgentNames.Returns, HttpMethod.Get, "policies",
            null, null, false, token).ConfigureAwait(false);
        if (policies.Length == 0 || policies.Any(policy => policy is null
                || string.IsNullOrWhiteSpace(policy.Id) || string.IsNullOrWhiteSpace(policy.Title)
                || string.IsNullOrWhiteSpace(policy.Text) || string.IsNullOrWhiteSpace(policy.Version))
            || policies.Select(policy => policy.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policies.Length)
            throw new ShopServiceException("Dati policy demo vuoti o non validi; nessun fallback locale.");

        return new(DemoClock.AsOf, DemoClock.CustomerId,
            "Dati sintetici di laboratorio, in sola lettura. Gli ordini degli altri clienti sono visibili solo per didattica: "
            + "questa pagina non autorizza l'accesso del cliente e non e uno strumento agente. "
            + "L'autorizzazione della chat e la conferma delle azioni restano invariate.",
            orders, policies.OrderByDescending(policy => policy.Priority).ToArray());
    }

    public async Task<IReadOnlyList<ProductFact>> SearchProductsAsync(string? query, decimal? maxPrice, int take, CancellationToken token)
    {
        var path = QueryPath("products", new() { Query = query, MaxPrice = maxPrice, Take = take });
        return await SendAsync<ProductFact[]>(AgentNames.Catalog, HttpMethod.Get, path, null, null, false, token).ConfigureAwait(false);
    }

    public Task<CatalogQueryResponse> QueryCatalogAsync(CatalogQueryRequest query, CancellationToken token) =>
        SendAsync<CatalogQueryResponse>(AgentNames.Catalog, HttpMethod.Get, QueryPath("catalog/query", query),
            null, null, false, token);

    public Task<CatalogFacetsResponse> GetCatalogFacetsAsync(CancellationToken token) =>
        SendAsync<CatalogFacetsResponse>(AgentNames.Catalog, HttpMethod.Get, "catalog/facets", null, null, false, token);

    public Task<ProductFact> GetProductAsync(int productId, CancellationToken token) =>
        SendAsync<ProductFact>(AgentNames.Catalog, HttpMethod.Get, $"products/{productId.ToString(CultureInfo.InvariantCulture)}", null, null, false, token);

    public Task<ShopOrder> GetOrderAsync(string orderId, string customerId, CancellationToken token) =>
        SendAsync<ShopOrder>(AgentNames.Orders, HttpMethod.Get, $"orders/{Uri.EscapeDataString(orderId)}", null, customerId, false, token);

    public Task<ReturnAssessment> AssessReturnAsync(string orderId, string reason, string customerId, CancellationToken token) =>
        SendAsync<ReturnAssessment>(AgentNames.Returns, HttpMethod.Post, "return-assessments",
            new ReturnOperationRequest(orderId, reason), customerId, false, token);

    public async Task<IReadOnlyList<ShopPolicy>> GetPoliciesAsync(CancellationToken token) =>
        await SendAsync<ShopPolicy[]>(AgentNames.Returns, HttpMethod.Get, "policies", null, null, false, token).ConfigureAwait(false);

    public Task<ReturnDraft> CreateReturnDraftAsync(string orderId, string reason, bool confirmed, string customerId, CancellationToken token) =>
        SendAsync<ReturnDraft>(AgentNames.Orders, HttpMethod.Post, "return-drafts",
            new ReturnOperationRequest(orderId, reason), customerId, confirmed, token);

    private static string QueryPath(string path, CatalogQueryRequest query)
    {
        path += $"?take={query.Take.ToString(CultureInfo.InvariantCulture)}";
        if (query.Query is not null) path += $"&query={Uri.EscapeDataString(query.Query)}";
        if (query.Category is not null) path += $"&category={Uri.EscapeDataString(query.Category)}";
        if (query.Color is not null) path += $"&color={Uri.EscapeDataString(query.Color)}";
        if (query.MaxPrice.HasValue) path += $"&maxPrice={query.MaxPrice.Value.ToString(CultureInfo.InvariantCulture)}";
        if (query.InStockOnly) path += "&inStockOnly=true";
        return path;
    }

    private async Task<T> SendAsync<T>(string role, HttpMethod method, string path, ReturnOperationRequest? body,
        string? customerId, bool confirmed, CancellationToken token)
    {
        var uri = new Uri(ServiceEndpoint(role), path);
        using var request = new HttpRequestMessage(method, uri);
        if (customerId is not null) request.Headers.Add(ShopHttpContract.CustomerHeader, customerId);
        if (confirmed) request.Headers.Add(ShopHttpContract.ConfirmationHeader, "true");
        if (body is not null) request.Content = JsonContent.Create(body, options: AgentJson.Options);
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Only known domain rejections may become recoverable tool errors. Transport/auth failures stop the run.
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                    or HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
                {
                    var problem = await response.Content.ReadFromJsonAsync<ServiceProblem>(ResponseJson, token).ConfigureAwait(false);
                    if (problem is not null && IsDomainRejection(problem.Code, response.StatusCode))
                        throw new DomainException(problem.Code!, SafeTelemetry.Text(problem.Detail ?? problem.Code));
                }
                throw new ShopServiceException($"Il servizio {role} ha restituito HTTP {(int)response.StatusCode}; nessun fallback locale.");
            }
            return await response.Content.ReadFromJsonAsync<T>(ResponseJson, token).ConfigureAwait(false)
                ?? throw new ShopServiceException($"Risposta vuota dal servizio {role}; nessun fallback locale.");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new ShopServiceException($"Chiamata HTTP al servizio {role} non riuscita: {SafeTelemetry.Text(error.Message)}", error);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new ShopServiceException($"Timeout del servizio {role}; nessun fallback locale.", error);
        }
    }

    private static bool IsDomainRejection(string? code, HttpStatusCode status) => (code, status) switch
    {
        ("product_not_found" or "order_not_found", HttpStatusCode.NotFound) => true,
        ("invalid_search" or "invalid_return_reason" or "invalid_request", HttpStatusCode.BadRequest) => true,
        ("confirmation_required", HttpStatusCode.Forbidden) => true,
        ("draft_conflict", HttpStatusCode.Conflict) => true,
        ("return_not_eligible" or "return_not_allowed", HttpStatusCode.UnprocessableEntity) => true,
        _ => false
    };

    private sealed record ServiceProblem(string? Code, string? Detail);
}
