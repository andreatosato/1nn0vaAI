using System.ClientModel.Primitives;
using System.Text.Json;

namespace Observatory.Agents;

// Captures billing evidence only; tracing and metrics come from the official SDK instrumentation.
public sealed class ProviderUsageCapturePolicy : PipelinePolicy
{
    private static readonly AsyncLocal<CaptureScope?> Current = new();

    public static CaptureScope BeginCapture()
    {
        var scope = new CaptureScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        Capture(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        Capture(message);
    }

    private static void Capture(PipelineMessage message)
    {
        if (Current.Value is not { } scope || !message.BufferResponse || message.Response is not { } response)
            return;
        try
        {
            // Read only usage before SDK numeric defaults erase the distinction between absent and zero.
            using var document = JsonDocument.Parse(response.Content.ToMemory());
            if (document.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                scope.Usage = usage.Clone();
        }
        catch (JsonException)
        {
            scope.Usage = null;
        }
    }

    public sealed class CaptureScope(CaptureScope? previous) : IDisposable
    {
        public JsonElement? Usage { get; internal set; }
        public void Dispose() => Current.Value = previous;
    }
}
