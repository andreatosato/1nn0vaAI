using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Agents.Tests;

/// <summary>Run limits, unbounded execution and fail-closed usage, with an offline chat client (no provider call).</summary>
public sealed class AgentRunnerTests
{
    [Fact]
    public async Task Bounded_runs_stop_at_the_model_call_limit_and_send_the_output_cap()
    {
        var (runner, model) = Create(toolCallsBeforeAnswer: 3);

        var error = await Assert.ThrowsAsync<DomainException>(() => Run(runner, Bounded(maxCalls: 2)));

        Assert.Equal("model_call_limit", error.Code);
        Assert.Equal(2, model.Calls.Count);
        Assert.All(model.Calls, options => Assert.Equal(64, options.MaxOutputTokens));
    }

    [Fact]
    public async Task Unbounded_runs_ignore_call_budget_and_output_limits_but_keep_the_ledger()
    {
        var (runner, model) = Create(toolCallsBeforeAnswer: 4);
        var events = new List<RunEvent>();

        var result = await Run(runner, Bounded(maxCalls: 2) with { UnboundedExecution = true, ApprovedBudgetUsd = null }, events);

        Assert.Equal(FakeChatClient.FinalAnswer, result.Answer);
        Assert.Equal(5, model.Calls.Count);
        Assert.All(model.Calls, options => Assert.Null(options.MaxOutputTokens));
        var calls = events.Where(item => item.Kind == "model.completed").Select(item => (ModelCallRecord)item.Data!).ToArray();
        Assert.Equal(5, calls.Length);
        Assert.All(calls, call => Assert.Equal("provider", call.UsageSource));
        Assert.All(calls, call => Assert.NotNull(call.EstimatedCostUsd));
        Assert.Single(events, item => item.Kind == "answer.delta");
    }

    [Fact]
    public async Task Unknown_provider_usage_stops_the_next_call_even_without_limits()
    {
        var (runner, model) = Create(toolCallsBeforeAnswer: 2, reportUsage: false);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Run(runner, Bounded(maxCalls: 10) with { UnboundedExecution = true, ApprovedBudgetUsd = null }));

        Assert.Equal("budget_exhausted", error.Code);
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task Unbounded_execution_requires_the_server_opt_in()
    {
        var (runner, _) = Create(toolCallsBeforeAnswer: 0, allowUnbounded: false);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Run(runner, Bounded(maxCalls: 2) with { UnboundedExecution = true, ApprovedBudgetUsd = null }));

        Assert.Equal("unbounded_disabled", error.Code);
    }

    private static RunConfiguration Bounded(int maxCalls) => new()
    {
        ModelProfileId = "gpt5", MaxModelCalls = maxCalls, MaxOutputTokens = 64, ApprovedBudgetUsd = 1m
    };

    private static Task<AgentExecutionResult> Run(AgentRunner runner, RunConfiguration configuration, List<RunEvent>? events = null)
    {
        var request = new AgentRunRequest
        {
            RunId = "run-limits", ConversationId = "conversation-limits", Technology = DemoTechnologies.Inline,
            Message = "ciao", Configuration = configuration
        };
        var agent = new AgentDefinition(AgentNames.Router, _ => "Istruzioni di prova.",
            _ => [AIFunctionFactory.Create(() => "pong", "ping")]) { PublishesAnswer = true };
        return runner.RunAsync(agent, request, [new ChatMessage(ChatRole.User, "ciao")],
            item => { events?.Add(item); return Task.CompletedTask; }, CancellationToken.None);
    }

    private static (AgentRunner Runner, FakeChatClient Model) Create(int toolCallsBeforeAnswer, bool reportUsage = true,
        bool allowUnbounded = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:AllowLive"] = "true",
            ["Demo:AllowUnboundedExecution"] = allowUnbounded.ToString(),
            ["AzureOpenAI:Endpoint"] = "https://fixture.invalid",
            ["Models:gpt5:Deployment"] = "fixture-only",
            ["Models:gpt5:InputPerMillion"] = "1",
            ["Models:gpt5:CachedInputPerMillion"] = "0.1",
            ["Models:gpt5:OutputPerMillion"] = "2",
            ["Models:gpt5:SourceUrl"] = "https://fixture.invalid/pricing",
            ["Models:gpt5:VerifiedAt"] = "2026-09-25",
            ["Models:gpt5:Capabilities:FunctionCalling"] = "true",
            ["Models:gpt5:Capabilities:MaxOutputTokens"] = "true"
        }).Build();
        var model = new FakeChatClient(toolCallsBeforeAnswer, reportUsage);
        var services = new ServiceCollection()
            .AddAgentRuntime(configuration)
            .AddSingleton<IChatClientProvider>(new FakeChatClientProvider(model))
            .BuildServiceProvider();
        return (services.GetRequiredService<AgentRunner>(), model);
    }

    private sealed class FakeChatClientProvider(FakeChatClient model) : IChatClientProvider
    {
        public IChatClient Create(ModelRegistration registration) => model;
    }

    /// <summary>Asks for the "ping" tool N times, then answers. Reports provider usage like Azure OpenAI.</summary>
    private sealed class FakeChatClient(int toolCallsBeforeAnswer, bool reportUsage) : IChatClient
    {
        public const string FinalAnswer = "Risposta finale.";
        private static readonly JsonElement Usage = JsonDocument.Parse(
            """{"prompt_tokens":100,"completion_tokens":10,"prompt_tokens_details":{"cached_tokens":50}}""").RootElement;

        public List<ChatOptions> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(options?.Clone() ?? new ChatOptions());
            if (reportUsage) ProviderUsageCapturePolicy.Record(Usage);
            var message = Calls.Count <= toolCallsBeforeAnswer
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{Calls.Count}", "ping")])
                : new ChatMessage(ChatRole.Assistant, FinalAnswer);
            return Task.FromResult(new ChatResponse(message));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
