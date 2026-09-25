using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static class BusinessEndpoints
{
    public static void MapBusinessEndpoints(this WebApplication app, string role)
    {
        var api = app.MapGroup("").WithTags(role)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        switch (role)
        {
            case AgentNames.Catalog:
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
                break;
            case AgentNames.Orders:
                api.MapGet("/demo-data/orders", (IShopData shop, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return TypedResults.Ok(shop.DemoOrders);
                }).WithName("DemoOrders")
                    .WithSummary("Read all synthetic orders for the teaching UI only; never an agent tool.")
                    .WithDescription("Includes other demo customers only for teaching. Transport access is still required; customer-scoped chat authorization and confirmation guards remain unchanged.");
                api.MapGet("/orders/{orderId}", (string orderId, HttpRequest request, IShopData shop) =>
                {
                    ValidateOrderId(orderId);
                    return TypedResults.Ok(shop.GetOrder(orderId, Customer(request)));
                }).WithName("GetOrder").WithSummary("Read an order for the trusted backend customer.")
                    .ProducesProblem(StatusCodes.Status404NotFound);
                api.MapPost("/return-drafts", (ReturnOperationRequest body, HttpRequest request, IShopData shop) =>
                {
                    Validate(body);
                    return TypedResults.Ok(shop.CreateReturnDraft(body.OrderId, body.Reason, Confirmed(request), Customer(request)));
                }).WithName("CreateReturnDraft")
                    .WithSummary("Create or return the same confirmed synthetic draft; never execute a refund.")
                    .WithDescription("200 for both initial creation and idempotent replay. No read-by-ID resource or Location is advertised.")
                    .ProducesProblem(StatusCodes.Status403Forbidden)
                    .ProducesProblem(StatusCodes.Status404NotFound)
                    .ProducesProblem(StatusCodes.Status409Conflict)
                    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
                break;
            case AgentNames.Returns:
                api.MapGet("/policies", (IShopData shop) => TypedResults.Ok(shop.Policies.OrderByDescending(policy => policy.Priority).ToArray()))
                    .WithName("GetPolicies").WithSummary("Read synthetic policies in priority order.");
                api.MapPost("/return-assessments", (ReturnOperationRequest body, HttpRequest request, IShopData shop) =>
                {
                    Validate(body);
                    return TypedResults.Ok(shop.AssessReturn(body.OrderId, body.Reason, Customer(request)));
                }).WithName("AssessReturn").WithSummary("Evaluate the current reason without creating a draft or refund.")
                    .ProducesProblem(StatusCodes.Status404NotFound);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(role));
        }

        var registry = app.Services.GetRequiredService<AgentModelRegistry>();
        var directory = Path.Combine(registry.SkillsDirectory, role, $"shop-{role}");
        MapSkillFile(api, $"/skills/shop-{role}/SKILL.md", Path.Combine(directory, "SKILL.md"), $"{role}Skill");
        if (role == AgentNames.Returns)
            MapSkillFile(api, "/skills/shop-returns/references/decision-checklist.md",
                Path.Combine(directory, "references", "decision-checklist.md"), "returnsChecklist");
    }

    private static void MapSkillFile(RouteGroupBuilder api, string route, string path, string name)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Pacchetto skill del servizio incompleto.", path);
        api.MapGet(route, async (CancellationToken token) =>
            TypedResults.Text(await File.ReadAllTextAsync(path, token), "text/markdown; charset=utf-8"))
            .WithName(name).WithSummary("Read this service's versioned integration skill resource; no scripts.");
    }

    private static string Customer(HttpRequest request)
    {
        var header = request.Headers[ShopServiceClient.CustomerHeader];
        var value = header.Count == 1 ? header[0] : null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new DomainException("customer_context_required", "Contesto cliente backend mancante o non valido.");
        return value;
    }

    private static bool Confirmed(HttpRequest request)
    {
        var header = request.Headers[ShopServiceClient.ConfirmationHeader];
        if (header.Count == 0) return false;
        if (header.Count != 1 || !bool.TryParse(header[0], out var confirmed))
            throw new DomainException("invalid_request", "Conferma backend non valida.");
        return confirmed;
    }

    private static void Validate(ReturnOperationRequest request)
    {
        ValidateOrderId(request.OrderId);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 128)
            throw new DomainException("invalid_request", "Il motivo e obbligatorio (massimo 128 caratteri).");
    }

    private static void ValidateOrderId(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 128)
            throw new DomainException("invalid_request", "ID ordine obbligatorio (massimo 128 caratteri).");
    }

    internal static int StatusFor(string code) => code switch
    {
        "product_not_found" or "order_not_found" => StatusCodes.Status404NotFound,
        "customer_context_required" => StatusCodes.Status401Unauthorized,
        "confirmation_required" => StatusCodes.Status403Forbidden,
        "draft_conflict" => StatusCodes.Status409Conflict,
        "return_not_eligible" or "return_not_allowed" => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest
    };
}
