using Observatory.Core;

namespace Observatory.AgentRuntime;

/// <summary>
/// The shared "how" of every shop tool: HTTP protocol events for the timeline, domain evidence and fail-closed errors.
/// The "what" (tool names, descriptions, parameters) is written by each agent in its own Tools folder.
/// </summary>
public static class ShopToolCall
{
    public static async Task<T> InvokeAsync<T>(IShopOperations shop, RunState state, string agent,
        string service, string operation, Func<Task<T>> invoke)
    {
        var endpoint = shop.ServiceEndpoint(service);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = null;
        if (endpoint is not null)
            await state.EmitAsync("protocol.request", agent, $"HTTP {service}: {operation}.", new
            {
                protocol = "HTTP", service, operation, url = endpoint.AbsoluteUri
            }).ConfigureAwait(false);
        try
        {
            var result = await invoke().ConfigureAwait(false);
            state.ObserveDomain(result);
            return result;
        }
        catch (Exception error)
        {
            failure = error;
            if (error is not DomainException) state.Fail(error);
            throw;
        }
        finally
        {
            if (endpoint is not null)
                await state.EmitAsync("protocol.response", agent, $"HTTP {service}: {operation}.", new
                {
                    protocol = "HTTP", service, operation, url = endpoint.AbsoluteUri,
                    durationMs = watch.Elapsed.TotalMilliseconds,
                    status = failure is OperationCanceledException ? "cancelled" : failure is null ? "completed" : "failed",
                    error = failure is null ? null : SafeTelemetry.Text(failure.Message)
                }).ConfigureAwait(false);
        }
    }
}
