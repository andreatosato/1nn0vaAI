using Microsoft.AspNetCore.Diagnostics;
using Observatory.Core;

namespace Observatory.Shop.Returns;

// Domain rejections become problem details that the agent tools can report as recoverable errors.
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        var (status, code, detail) = exception switch
        {
            DomainException domain => (StatusFor(domain.Code), domain.Code, domain.Message),
            BadHttpRequestException bad => (bad.StatusCode, "invalid_request", "Richiesta non valida o troppo grande."),
            _ => (StatusCodes.Status500InternalServerError, "service_error", "Operazione del servizio non riuscita. Nessun fallback; consultare i log backend.")
        };
        logger.LogWarning(exception, "Returns request rejected with {Code}.", code);
        await Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
        return true;
    }

    private static int StatusFor(string code) => code switch
    {
        "order_not_found" => StatusCodes.Status404NotFound,
        "customer_context_required" => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest
    };
}
