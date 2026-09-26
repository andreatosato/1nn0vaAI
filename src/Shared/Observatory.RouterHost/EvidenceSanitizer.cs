using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Observatory.Core;

namespace Observatory.Api;

public sealed partial class EvidenceSanitizer
{
    private readonly string[] secrets;
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "api-key", "apikey", "api_key", "x-api-key",
        "password", "secret", "clientsecret", "client_secret", "access_token", "refresh_token",
        "id_token", "credential", "credentials", "connectionstring", "cookie", "set-cookie",
        "token", "accessToken", "refreshToken", "idToken", "subscription-key", "subscriptionKey", "sig"
    };

    public EvidenceSanitizer(IConfiguration configuration)
    {
        secrets = configuration.AsEnumerable()
            .Where(entry => IsSensitive(entry.Key.Split(':').Last()) && !string.IsNullOrEmpty(entry.Value))
            .Select(entry => entry.Value!).Distinct(StringComparer.Ordinal).ToArray();
    }

    public JsonNode? Sanitize(object? value)
    {
        var node = JsonSerializer.SerializeToNode(value, ApiJson.Options);
        return Clean(node, null);
    }

    public string Text(string value)
    {
        foreach (var secret in secrets) value = value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        value = Bearer().Replace(value, "$1[REDACTED]");
        value = CredentialAssignment().Replace(value, "$1[REDACTED]");
        value = RecognizableKey().Replace(value, "[REDACTED]");
        return value;
    }

    public RunEvent Event(RunEvent value) => value with
    {
        Message = Text(value.Message),
        Data = value.Data is null ? null : Sanitize(value.Data)
    };

    public ModelCallRecord Call(ModelCallRecord value) =>
        Sanitize(value)!.Deserialize<ModelCallRecord>(ApiJson.Options)!;

    private JsonNode? Clean(JsonNode? node, string? key)
    {
        if (key is not null && IsSensitive(key)) return JsonValue.Create("[REDACTED]");
        if (node is JsonObject obj)
        {
            var clean = new JsonObject();
            foreach (var property in obj) clean[property.Key] = Clean(property.Value, property.Key);
            return clean;
        }
        if (node is JsonArray array)
        {
            var clean = new JsonArray();
            foreach (var item in array) clean.Add(Clean(item, key));
            return clean;
        }
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (key is "requestBody" or "responseBody" or "exactRequestBody" && LooksLikeJson(text))
            {
                try
                {
                    var parsed = JsonNode.Parse(text);
                    var clean = Clean(parsed, null);
                    return JsonValue.Create(JsonNode.DeepEquals(parsed, clean) ? text : clean?.ToJsonString(ApiJson.Options));
                }
                catch (JsonException) { return JsonValue.Create(Text(text)); }
            }
            if (Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "https" or "http")
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
                var safeQuery = string.Join("&", query.SelectMany(pair => pair.Value.Select(item =>
                    Uri.EscapeDataString(pair.Key) + "=" +
                    Uri.EscapeDataString(IsSensitive(pair.Key) ? "[REDACTED]" : Text(item ?? "")))));
                var safe = new UriBuilder(url) { UserName = "", Password = "", Query = safeQuery, Fragment = "" };
                return JsonValue.Create(Text(safe.Uri.AbsoluteUri));
            }
            return JsonValue.Create(Text(text));
        }
        return node?.DeepClone();
    }

    private static bool IsSensitive(string key) => SensitiveKeys.Contains(key) ||
        key.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("Password", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("ClientSecret", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeJson(string value) => value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('[');

    [GeneratedRegex(@"(?i)((?:Bearer|Basic)\s+)[a-z0-9._~+/\-=]+", RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i)((?:api[-_]?key|client[-_]?secret|access[-_]?token|password|sig)[""']?\s*[=:]\s*[""']?)[^\s""'&,;<>]+", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignment();

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.CultureInvariant)]
    private static partial Regex RecognizableKey();
}
