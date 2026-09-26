using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Observatory.Core;
using Observatory.ServiceDefaults;

namespace Observatory.AgentRuntime;

/// <summary>What makes one agent different from another: its role, its instructions and its tools.</summary>
public sealed record AgentDefinition(
    string Role,
    Func<AgentRunRequest, string> Instructions,
    Func<RunState, IList<AITool>> Tools)
{
    /// <summary>Optional context provider for one run, for example the native Agent Skills provider.</summary>
    public Func<RunState, AIContextProvider>? Context { get; init; }

    /// <summary>The root router publishes its final answer to the chat timeline.</summary>
    public bool PublishesAnswer { get; init; }
}

/// <summary>
/// Runs one Microsoft Agent Framework ChatClientAgent for one run: model pipeline, tool invocation with evidence,
/// run limits and native OpenTelemetry. Agents describe themselves with an <see cref="AgentDefinition"/>.
/// </summary>
public sealed class AgentRunner(
    AgentModelRegistry registry,
    ModelClientFactory models,
    ILoggerFactory loggerFactory,
    IServiceProvider services)
{
    public async Task<AgentExecutionResult> RunAsync(AgentDefinition agent, AgentRunRequest request,
        IEnumerable<ChatMessage> messages, Func<RunEvent, Task> emit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emit);
        registry.Validate(request);
        request = Freeze(request);
        cancellationToken.ThrowIfCancellationRequested();
        var state = new RunState(request, emit);
        using var scope = new AgentResources();
        var runnable = CreateAgent(agent, state, scope);
        var response = await runnable.RunAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        state.ThrowIfFaulted();
        var result = state.Result(response.Text);
        if (agent.PublishesAnswer)
            await state.EmitAsync("answer.delta", agent.Role, result.Answer, new { text = result.Answer, buffered = true }).ConfigureAwait(false);
        return result;
    }

    private static AgentRunRequest Freeze(AgentRunRequest request) => request with
    {
        Configuration = request.Configuration with { AgentModels = new(request.Configuration.AgentModels, StringComparer.Ordinal) },
        History = request.History.Select(message => message with
        {
            ProductIds = message.ProductIds.ToArray(),
            Sources = message.Sources.ToArray()
        }).ToArray()
    };

    private AIAgent CreateAgent(AgentDefinition agent, RunState state, AgentResources resources)
    {
        var role = agent.Role;
        var tools = agent.Tools(state);
        var client = new FunctionInvokingChatClient(models.Create(state, role), loggerFactory, services)
        {
            AllowConcurrentInvocation = false,
            MaximumIterationsPerRequest = state.Request.Configuration.UnboundedExecution ? int.MaxValue : state.Request.Configuration.MaxModelCalls,
            FunctionInvoker = async (context, token) =>
            {
                token.ThrowIfCancellationRequested();
                context.Arguments.Services ??= services;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                object? result = null;
                Exception? failure = null;
                try
                {
                    result = await context.Function.InvokeAsync(context.Arguments, token).ConfigureAwait(false);
                    if (context.Function.Name == AgentSkillsProvider.LoadSkillToolName)
                        await state.EmitAsync("skill.loaded", role, "Skill caricata dal provider nativo, dopo richiesta del modello.", new
                        {
                            provider = nameof(AgentSkillsProvider),
                            arguments = SafeTelemetry.Snapshot(context.Arguments)
                        }).ConfigureAwait(false);
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
                    await state.EmitAsync("tool.called", role, context.Function.Name, new
                    {
                        name = context.Function.Name,
                        callId = context.CallContent.CallId,
                        arguments = SafeTelemetry.Snapshot(context.Arguments),
                        result = SafeTelemetry.Snapshot(result),
                        durationMs = watch.Elapsed.TotalMilliseconds,
                        status = failure is null ? "completed" : "failed",
                        error = failure is null ? null : SafeTelemetry.Text(failure.Message)
                    }).ConfigureAwait(false);
                }
            }
        };
        resources.Add(client);
        var options = new ChatClientAgentOptions
        {
            Name = role,
            Description = $"Agente {role} del negozio sintetico; AIAgent di Microsoft Agent Framework.",
            ChatOptions = new ChatOptions
            {
                Instructions = agent.Instructions(state.Request),
                Tools = tools,
                MaxOutputTokens = state.Request.Configuration.UnboundedExecution ? null : state.Request.Configuration.MaxOutputTokens,
                AllowMultipleToolCalls = false
            }
        };
        if (agent.Context is not null)
        {
            var provider = agent.Context(state);
            if (provider is IDisposable disposable) resources.Add(disposable);
            options.AIContextProviders = [provider];
        }
        AIAgent inner = new ChatClientAgent(client, options, loggerFactory, services);
        return inner.AsBuilder()
            .Use(async (messages, session, runOptions, next, token) =>
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                await state.EmitAsync("agent.started", role, "Microsoft Agent Framework AIAgent avviato.", new
                {
                    implementation = nameof(ChatClientAgent),
                    modelProfile = registry.ForAgent(state.Request, role).Model.Id,
                    promptProfile = state.Request.Configuration.PromptProfile
                }).ConfigureAwait(false);
                Exception? failure = null;
                try
                {
                    return await next.RunAsync(messages, session, runOptions, token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = error;
                    throw;
                }
                finally
                {
                    await state.EmitAsync("agent.completed", role, failure is null ? "Agente completato." : "Agente terminato con errore.", new
                    {
                        durationMs = started.Elapsed.TotalMilliseconds,
                        status = failure is OperationCanceledException ? "cancelled" : failure is null ? "completed" : "failed",
                        error = failure is null ? null : SafeTelemetry.Text(failure.Message)
                    }).ConfigureAwait(false);
                }
            }, null)
            .UseObservatoryTelemetry()
            .Build();
    }

    private sealed class AgentResources : IDisposable
    {
        private readonly List<IDisposable> _items = [];
        public void Add(IDisposable item) => _items.Add(item);
        public void Dispose()
        {
            for (var index = _items.Count - 1; index >= 0; index--) _items[index].Dispose();
        }
    }
}
