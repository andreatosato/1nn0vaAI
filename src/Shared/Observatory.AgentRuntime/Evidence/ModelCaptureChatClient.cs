using Microsoft.Extensions.AI;
using Observatory.Core;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Observatory.Agents;

// Application evidence beneath the native chat span: no custom spans, meters or exporters.
internal sealed class ModelCaptureChatClient(IChatClient inner, RunState state, string agent, ModelRegistration registration)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.BeforeModelCall();
        var input = messages.ToArray();
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        ChatResponse? response = null;
        Exception? failure = null;
        using var usageCapture = ProviderUsageCapturePolicy.BeginCapture();
        try
        {
            if (registration.UseNonReasoningChatTools)
            {
                options = options?.Clone() ?? new ChatOptions();
                options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
            }
            response = await base.GetResponseAsync(input, options, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            watch.Stop();
            var record = new ModelCallRecord
            {
                RunId = state.Request.RunId,
                Agent = agent,
                ModelProfileId = registration.Model.Id,
                Mode = state.Request.Configuration.Mode,
                ModelId = response?.ModelId ?? registration.Model.ModelId,
                Deployment = registration.Model.Deployment,
                ProviderResponseId = response?.ResponseId,
                TraceId = Activity.Current?.TraceId.ToString(),
                SpanId = Activity.Current?.SpanId.ToString(),
                StartedAt = started,
                DurationMs = watch.Elapsed.TotalMilliseconds,
                UsageSource = "unknown",
                CaptureKind = "logical",
                Status = failure is OperationCanceledException ? "cancelled" : failure is null ? "completed" : "failed",
                Request = new
                {
                    captureKind = "logical",
                    provider = registration.Provider,
                    promptProfile = state.Request.Configuration.PromptProfile,
                    promptBlocks = state.Request.Configuration.PromptBlocks,
                    promptNotice = state.Request.Configuration.PromptProfile is "gpt5" or "gpt6" ? registration.PromptNotice : "Generic controlled prompt experiment; no model ranking.",
                    historyStrategy = state.Request.Configuration.HistoryStrategy,
                    instructions = SafeTelemetry.Text(options?.Instructions),
                    messages = SafeTelemetry.Messages(input),
                    tools = options?.Tools?.OfType<AIFunction>().Select(tool => new
                    {
                        tool.Name, tool.Description, parameters = SafeTelemetry.Snapshot(tool.JsonSchema)
                    }).ToArray(),
                    parameters = new
                    {
                        options?.MaxOutputTokens, options?.AllowMultipleToolCalls,
                        reasoningEffort = options?.Reasoning?.Effort.ToString()
                    }
                },
                Response = response is null ? null : new
                {
                    messages = SafeTelemetry.Messages(response.Messages),
                    finishReason = response.FinishReason?.Value
                },
                Error = failure is null ? null : SafeTelemetry.Text(failure.Message)
            };
            record = ProviderUsageReader.Apply(record, usageCapture.Usage);
            record = TokenCostCalculator.Price(record, registration.Model.Pricing);
            await state.EmitAsync("model.completed", agent, "Chiamata provider; cattura logica, non wire.", record).ConfigureAwait(false);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }
}
