using Microsoft.AspNetCore.Diagnostics;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

public static class ApiErrors
{
    public static void UseObservatoryErrors(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, code, message) = error switch
            {
                ApiException api => (api.Status, api.Code, api.Message),
                ShopServiceException => (502, "shop_service_unavailable", "The shop service returned unavailable or invalid data. No local fallback was performed."),
                DomainException domain => (422, domain.Code, domain.Message),
                BadHttpRequestException bad => (bad.StatusCode, "invalid_request", "The request is malformed or too large."),
                _ => (500, "internal_error", "The operation failed; inspect server diagnostics.")
            };
            var sanitizer = context.RequestServices.GetRequiredService<EvidenceSanitizer>();
            await Results.Problem(statusCode: status, title: code, detail: sanitizer.Text(message),
                extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }));
        app.UseStatusCodePages(async context =>
        {
            await Results.Problem(statusCode: context.HttpContext.Response.StatusCode,
                title: "http_error", detail: "The requested operation is not available.").ExecuteAsync(context.HttpContext);
        });
    }
}
