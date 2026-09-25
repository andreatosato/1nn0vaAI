using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Observatory.Agents;

// Sanitizes persisted Inspector evidence, not OpenTelemetry payloads.
internal static partial class SafeTelemetry
{
    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey", "api-key", "authorization", "password", "secret", "connectionString",
        "sharedSecret", "Agents:SharedSecret", "Agents__SharedSecret", AgentTransportAccess.HeaderName,
        "thumbnail", "images", "image", "imageUrl", "image_url", "base64", "rawRepresentation"
    };

    public static object Snapshot(object? value)
    {
        var element = JsonSerializer.SerializeToElement(value, AgentJson.Options);
        return Clean(element)!;
    }

    public static string Text(string? text) => CredentialPattern().Replace(text ?? "", "[REDACTED]");

    public static object Messages(IEnumerable<ChatMessage> messages) => Snapshot(messages.Select(message => new
    {
        role = message.Role.Value,
        content = message.Contents.Select<AIContent, object>(content => content switch
        {
            TextContent text => new { type = "text", text = text.Text },
            FunctionCallContent call => new { type = "function_call", callId = call.CallId, name = call.Name, arguments = call.Arguments },
            FunctionResultContent result => new { type = "function_result", callId = result.CallId, result = Snapshot(result.Result) },
            _ => new { type = content.GetType().Name, omitted = true }
        })
    }));

    private static object? Clean(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Where(property => !ExcludedProperties.Contains(property.Name))
            .ToDictionary(property => property.Name, property => Clean(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(Clean).ToArray(),
        JsonValueKind.String => Text(element.GetString()),
        JsonValueKind.Number => element.Clone(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    [GeneratedRegex("""(?i)(?:Bearer\s+[a-z0-9._~+/\-=]+|(?:api[-_]?key|password|secret|authorization|sharedSecret|X-Observatory-A2A-Key)["']?\s*[=:]\s*["']?[^\s,;"'}]+|sk-[a-z0-9_-]{12,}|data:image/[a-z0-9.+-]+;base64,[a-z0-9+/=]+)""")]
    private static partial Regex CredentialPattern();
}
