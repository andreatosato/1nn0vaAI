using System.Text.Json;
using Observatory.Core;

namespace Observatory.Agents;

public static class ProviderUsageReader
{
    public static ModelCallRecord Apply(ModelCallRecord call, JsonElement? providerUsage)
    {
        if (providerUsage is not { ValueKind: JsonValueKind.Object } usage)
            return call with { UsageSource = "unknown" };
        var inputDetails = usage.TryGetProperty("prompt_tokens_details", out var input) ? input : default;
        var outputDetails = usage.TryGetProperty("completion_tokens_details", out var output) ? output : default;
        return call with
        {
            InputTokens = Count(usage, "prompt_tokens"),
            OutputTokens = Count(usage, "completion_tokens"),
            CachedInputTokens = Count(inputDetails, "cached_tokens"),
            CacheWriteTokens = Count(inputDetails, "cache_write_tokens"),
            ReasoningTokens = Count(outputDetails, "reasoning_tokens"),
            UsageSource = "provider",
            RawUsage = usage.Clone()
        };
    }

    private static long? Count(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : null;
}
