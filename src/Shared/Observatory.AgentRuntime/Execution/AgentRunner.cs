using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Observatory.Core;

namespace Observatory.Agents;

public sealed class AgentSession(
    AgentModelRegistry registry,
    ModelProviderFactory providers,
    ILoggerFactory loggerFactory,
    IServiceProvider services)
{
    public const string ActivitySourceName = "Observatory.Agents";

    public async Task<AgentExecutionResult> RunAsync(
        string role, AgentRunRequest request, IEnumerable<ChatMessage> messages,
        Func<RunState, IList<AITool>> tools, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken,
        Func<AIContextProvider>? contextProvider = null)
    {
        ArgumentNullException.ThrowIfNull(emit);
        registry.Validate(request);
        request = Freeze(request);
        cancellationToken.ThrowIfCancellationRequested();
        var state = new RunState(request, emit);
        using var scope = new AgentResources();
        var agent = CreateAgent(role, tools(state), state, scope, contextProvider);
        var response = await agent.RunAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false);
        state.ThrowIfFaulted();
        var result = state.Result(response.Text);
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

    private AIAgent CreateAgent(string role, IList<AITool> tools, RunState state, AgentResources resources,
        Func<AIContextProvider>? contextProvider)
    {
        var client = new FunctionInvokingChatClient(providers.Create(state, role), loggerFactory, services)
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
                Instructions = AgentPrompts.Instructions(role, state.Request),
                Tools = tools,
                MaxOutputTokens = state.Request.Configuration.UnboundedExecution ? null : state.Request.Configuration.MaxOutputTokens,
                AllowMultipleToolCalls = false
            }
        };
        if (contextProvider is not null)
        {
            var provider = contextProvider();
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
            .UseOpenTelemetry(ActivitySourceName, telemetry => telemetry.EnableSensitiveData = false)
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
