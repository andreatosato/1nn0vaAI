using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Agents.Tests;

/// <summary>
/// Billing evidence comes from the provider's raw usage through the real Azure OpenAI SDK pipeline (offline HTTP):
/// missing counts stay missing, explicit zeros stay zero, malformed counts fail closed.
/// </summary>
public sealed class ProviderUsageTests
{
    private const string Payload = """
        {"id":"fixture","object":"chat.completion","created":1,"model":"fixture",
         "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":1000,"completion_tokens":100,"total_tokens":1100,
           "prompt_tokens_details":{"cached_tokens":200,"cache_write_tokens":300},
           "completion_tokens_details":{"reasoning_tokens":20}}}
        """;

    [Fact]
    public async Task Raw_provider_usage_is_preserved_including_cache_writes_and_reasoning()
    {
        var call = await Read(Payload);

        Assert.Equal((1000L, 100L, 200L, 300L, 20L), (call.InputTokens, call.OutputTokens, call.CachedInputTokens, call.CacheWriteTokens, call.ReasoningTokens));
        Assert.Equal("provider", call.UsageSource);
        Assert.Equal(300, ((JsonElement)call.RawUsage!).GetProperty("prompt_tokens_details").GetProperty("cache_write_tokens").GetInt64());
    }

    [Theory]
    [InlineData(",\"cache_write_tokens\":300", "", "cacheWrite")]
    [InlineData("\"cached_tokens\":200,", "", "cached")]
    [InlineData("\"prompt_tokens\":1000,", "", "input")]
    [InlineData("\"completion_tokens\":100,", "", "output")]
    [InlineData("\"cache_write_tokens\":300", "\"cache_write_tokens\":\"300\"", "cacheWrite")]
    public async Task Missing_or_malformed_counts_are_never_manufactured(string remove, string replacement, string field)
    {
        var call = await Read(Payload.Replace(remove, replacement));

        var value = field switch
        {
            "cacheWrite" => call.CacheWriteTokens, "cached" => call.CachedInputTokens,
            "input" => call.InputTokens, _ => call.OutputTokens
        };
        Assert.Null(value);
    }

    [Fact]
    public async Task Explicit_zero_is_distinct_from_missing_and_concurrent_calls_do_not_leak()
    {
        var calls = await Task.WhenAll(Read(Payload), Read(Payload.Replace("\"cache_write_tokens\":300", "\"cache_write_tokens\":0")));

        Assert.Equal(300, calls[0].CacheWriteTokens);
        Assert.Equal(0, calls[1].CacheWriteTokens);
    }

    private static async Task<ModelCallRecord> Read(string payload)
    {
        using var http = new HttpClient(new UsageHttpHandler(payload));
        var options = new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) };
        options.AddPolicy(new ProviderUsageCapturePolicy(), PipelinePosition.PerCall);
        using var client = new AzureOpenAIClient(new Uri("https://fixture.invalid"), new ApiKeyCredential("fixture-only"), options)
            .GetChatClient("fixture").AsIChatClient();
        using var capture = ProviderUsageCapturePolicy.BeginCapture();
        await client.GetResponseAsync("offline fixture");
        return ProviderUsageReader.Apply(new() { RunId = "fixture", Agent = "router", ModelProfileId = "gpt5", Mode = "live" }, capture.Usage);
    }

    private sealed class UsageHttpHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
    }
}
