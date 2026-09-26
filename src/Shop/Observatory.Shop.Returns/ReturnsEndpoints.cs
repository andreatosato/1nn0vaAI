using Observatory.Core;

namespace Observatory.Shop.Returns;

public static class ReturnsEndpoints
{
    public static void MapReturnsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("").WithTags("returns")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        api.MapGet("/policies", (IShopData shop) => TypedResults.Ok(shop.Policies.OrderByDescending(policy => policy.Priority).ToArray()))
            .WithName("GetPolicies").WithSummary("Read synthetic policies in priority order.");

        api.MapPost("/return-assessments", (ReturnOperationRequest body, HttpRequest request, IShopData shop) =>
        {
            Validate(body);
            return TypedResults.Ok(shop.AssessReturn(body.OrderId, body.Reason, Customer(request)));
        }).WithName("AssessReturn").WithSummary("Evaluate the current reason without creating a draft or refund.")
            .ProducesProblem(StatusCodes.Status404NotFound);
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

    private static void Validate(ReturnOperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId) || request.OrderId.Length > 128)
            throw new DomainException("invalid_request", "ID ordine obbligatorio (massimo 128 caratteri).");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 128)
            throw new DomainException("invalid_request", "Il motivo e obbligatorio (massimo 128 caratteri).");
    }
}
