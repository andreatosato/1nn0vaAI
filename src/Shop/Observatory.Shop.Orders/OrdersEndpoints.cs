using Observatory.Core;

namespace Observatory.Shop.Orders;

public static class OrdersEndpoints
{
    public static void MapOrdersEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("").WithTags("orders")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        api.MapGet("/demo-data/orders", (IShopData shop, CancellationToken token) =>
        {
            token.ThrowIfCancellationRequested();
            return TypedResults.Ok(shop.DemoOrders);
        }).WithName("DemoOrders")
            .WithSummary("Read all synthetic orders for the teaching UI only; never an agent tool.")
            .WithDescription("Includes other demo customers only for teaching. Customer-scoped chat authorization and confirmation guards remain unchanged.");

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
    }

    private static string Customer(HttpRequest request)
    {
        var header = request.Headers[ShopHttpContract.CustomerHeader];
        var value = header.Count == 1 ? header[0] : null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new DomainException("customer_context_required", "Contesto cliente backend mancante o non valido.");
        return value;
    }

    private static bool Confirmed(HttpRequest request)
    {
        var header = request.Headers[ShopHttpContract.ConfirmationHeader];
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
}
