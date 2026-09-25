using System.ClientModel.Primitives;
using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static partial class OfflineSelfTests
{
    private static async Task ValidateProviderUsage()
    {
        const string json = """
            {"id":"fixture","object":"chat.completion","created":1,"model":"fixture",
             "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":1000,"completion_tokens":100,"total_tokens":1100,
               "prompt_tokens_details":{"cached_tokens":200,"cache_write_tokens":300},
               "completion_tokens_details":{"reasoning_tokens":20}}}
            """;
        async Task<ModelCallRecord> Read(string payload)
        {
            using var http = new HttpClient(new UsageHttpHandler(payload));
            var options = new AzureOpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(http),
                RetryPolicy = new ClientRetryPolicy(0)
            };
            options.AddPolicy(new ProviderUsageCapturePolicy(), PipelinePosition.PerCall);
            using var client = new AzureOpenAIClient(new Uri("https://fixture.invalid"), new ApiKeyCredential("fixture-only"), options)
                .GetChatClient("fixture").AsIChatClient();
            using var capture = ProviderUsageCapturePolicy.BeginCapture();
            await client.GetResponseAsync("offline fixture", new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
            });
            return ProviderUsageReader.Apply(new()
            {
                RunId = "fixture", Agent = "router", ModelProfileId = "gpt6-sol", Mode = "live"
            }, capture.Usage);
        }
        var actual = await Read(json);
        Check(actual.InputTokens == 1000 && actual.OutputTokens == 100 && actual.CachedInputTokens == 200 &&
            actual.CacheWriteTokens == 300 && actual.ReasoningTokens == 20 && actual.UsageSource == "provider",
            "Real Azure SDK/MEAI pipeline preserves raw provider cache-write usage.");
        Check(((JsonElement)actual.RawUsage!).GetProperty("prompt_tokens_details").GetProperty("cache_write_tokens").GetInt64() == 300,
            "Ledger raw usage retains the provider's exact cache-write field.");
        Check((await Read(json.Replace(",\"cache_write_tokens\":300", ""))).CacheWriteTokens is null,
            "Missing provider cache-write field is never manufactured as zero.");
        Check((await Read(json.Replace("\"cache_write_tokens\":300", "\"cache_write_tokens\":0"))).CacheWriteTokens == 0,
            "Explicit provider zero writes is distinguishable from missing usage.");
        Check((await Read(json.Replace("\"cache_write_tokens\":300", "\"cache_write_tokens\":\"300\""))).CacheWriteTokens is null,
            "Malformed provider counts fail closed without string coercion.");
        Check((await Read(json.Replace("\"cached_tokens\":200,", ""))).CachedInputTokens is null,
            "Missing provider cached-input count is not the SDK's synthesized zero.");
        Check((await Read(json.Replace("\"prompt_tokens\":1000,", ""))).InputTokens is null,
            "Missing provider input count is not the SDK's synthesized zero.");
        Check((await Read(json.Replace("\"completion_tokens\":100,", ""))).OutputTokens is null,
            "Missing provider output count is not the SDK's synthesized zero.");
        var concurrent = await Task.WhenAll(Read(json), Read(json.Replace("\"cache_write_tokens\":300", "\"cache_write_tokens\":0")));
        Check(concurrent[0].CacheWriteTokens == 300 && concurrent[1].CacheWriteTokens == 0,
            "Concurrent provider captures do not leak usage across calls.");
        Console.WriteLine("PASS SDK provider usage: input/output, raw cached/write/reasoning counts, missing/zero/malformed fields.");
    }

    private sealed class UsageHttpHandler(string payload) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Check(document.RootElement.GetProperty("reasoning_effort").GetString() == "none",
                "MEAI sends the explicitly configured nonreasoning Chat Completions option.");
            await Task.Yield();
            return new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
}
