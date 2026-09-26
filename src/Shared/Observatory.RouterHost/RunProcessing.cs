using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Observatory.Core;

namespace Observatory.Api;

public sealed class RunCoordinator(EvidenceStore store, ObservatorySettings settings, IShopCatalog data)
{
    private readonly Channel<byte> wakeups = Channel.CreateBounded<byte>(new BoundedChannelOptions(settings.QueueCapacity)
    {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly ConcurrentDictionary<string, CancellationTokenSource> running = new();

    public SubmissionResult Submit(string conversationId, SubmitTurnRequest request, string exactBody,
        string? scenarioId = null, string? experimentId = null)
    {
        settings.Validate(request);
        var submitted = store.Submit(conversationId, settings.Snapshot(request, exactBody, data), scenarioId, experimentId);
        Signal();
        return submitted;
    }

    public RunRecord Cancel(string id)
    {
        var run = store.Cancel(id);
        if (running.TryGetValue(id, out var source))
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { /* Completion won the race with cancellation. */ }
        }
        Signal();
        return run;
    }

    public async Task<RunRecord> WaitForCompletion(string id, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = store.GetRun(id);
            if (EvidenceStore.IsTerminal(run.Status)) return run;
            await Task.Delay(100, cancellationToken);
        }
    }

    internal void Register(string id, CancellationTokenSource source) => running[id] = source;
    internal void Unregister(string id) => running.TryRemove(id, out _);
    internal void Signal() => wakeups.Writer.TryWrite(0);
    internal async Task WaitForSignal(CancellationToken token)
    {
        await wakeups.Reader.ReadAsync(token);
        while (wakeups.Reader.TryRead(out _)) { }
    }
}

public sealed class RunWorker(EvidenceStore store, RunCoordinator coordinator, IAgentRuntime runtime,
    ObservatorySettings settings, EvidenceSanitizer sanitizer, ILogger<RunWorker> logger) : BackgroundService
{
    private static readonly ActivitySource Activities = new("Observatory.Api");

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        store.RecoverInterrupted();
        coordinator.Signal();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = new List<Task>();
        Task? signal = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var finished in active.Where(t => t.IsCompleted).ToArray())
                {
                    await finished;
                    active.Remove(finished);
                }
                while (active.Count < settings.Workers && !stoppingToken.IsCancellationRequested)
                {
                    var run = store.ClaimNext();
                    if (run is null) break;
                    active.Add(ProcessAsync(run, stoppingToken));
                }
                signal ??= coordinator.WaitForSignal(stoppingToken);
                var completed = await Task.WhenAny(active.Append(signal));
                if (completed == signal) { await signal; signal = null; }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await Task.WhenAll(active);
        }
    }

    private async Task ProcessAsync(RunRecord run, CancellationToken stoppingToken)
    {
        using var activity = Activities.StartActivity("observatory.run", ActivityKind.Internal);
        activity?.SetTag("observatory.run.id", run.Id);
        activity?.SetTag("observatory.technology", run.Technology);
        activity?.SetTag("observatory.mode", run.Configuration.Mode);
        var elapsed = Stopwatch.StartNew();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!run.Configuration.UnboundedExecution) cancellation.CancelAfter(settings.RunTimeout);
        coordinator.Register(run.Id, cancellation);
        string? accountingStopReason = null;
        try
        {
            if (store.GetRun(run.Id, false).Status == "cancelling") cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            settings.ValidateConfiguration(run.Configuration);
            var request = new AgentRunRequest
            {
                RunId = run.Id, ConversationId = run.ConversationId, Technology = run.Technology,
                Message = run.Message, Configuration = run.Configuration, History = store.HistoryBefore(run)
            };
            var result = await runtime.ExecuteAsync(request, value =>
            {
                if (value.RunId != run.Id) throw new InvalidDataException("The runtime emitted an event for another run.");
                if (string.IsNullOrWhiteSpace(value.Kind)) throw new InvalidDataException("Runtime event kind is missing.");
                if (value.Kind.StartsWith("run.", StringComparison.Ordinal))
                    throw new InvalidDataException("Only the API owns run lifecycle events.");
                var clean = value.Kind == "model.completed" && value.Data is ModelCallRecord call
                    ? value with { Data = sanitizer.Call(call), Message = sanitizer.Text(value.Message) }
                    : sanitizer.Event(value);
                var storedEvent = store.AppendEvent(clean, elapsed.Elapsed.TotalMilliseconds);
                if (value.Kind == "model.completed" && run.Configuration.Mode == "live")
                {
                    var current = store.GetRun(run.Id);
                    string? reason = null;
                    if (!run.Configuration.UnboundedExecution && current.Calls.Count > run.Configuration.MaxModelCalls)
                        reason = "Runtime exceeded the approved model-call limit.";
                    else if (storedEvent.Data is ModelCallRecord { Status: "completed" })
                    {
                        if (current.CostStatus != "priced")
                            reason = "Provider usage or configured pricing is incomplete. Further paid calls are blocked.";
                        else if (!run.Configuration.UnboundedExecution && current.EstimatedCostUsd >= run.Configuration.ApprovedBudgetUsd)
                            reason = "Approved run budget reached. Further paid calls are blocked.";
                    }
                    if (reason is not null)
                    {
                        Interlocked.CompareExchange(ref accountingStopReason, reason, null);
                        if (!cancellation.IsCancellationRequested)
                        {
                            try { cancellation.Cancel(); }
                            catch (AggregateException exception)
                            {
                                logger.LogError("Run {RunId} cancellation callback failed while draining evidence: {Error}",
                                    run.Id, sanitizer.Text(exception.Message));
                            }
                        }
                    }
                }
                // Cancellation stops future work, not the import of calls already completed remotely.
                return Task.CompletedTask;
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (result is null || string.IsNullOrWhiteSpace(result.Answer))
                throw new InvalidDataException("The runtime completed without an answer.");
            store.Finish(run.Id, "completed", result, null, elapsed.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var state = store.GetRun(run.Id, false).Status;
            var userCancelled = state == "cancelling";
            var error = userCancelled ? "Cancelled by user."
                : accountingStopReason ?? (stoppingToken.IsCancellationRequested
                    ? "Host stopped during execution; not automatically retried."
                    : "Execution exceeded the configured timeout; partial evidence is preserved.");
            store.Finish(run.Id, userCancelled ? "cancelled" : "failed", null, error, elapsed.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, error);
        }
        catch (Exception exception)
        {
            var error = sanitizer.Text(accountingStopReason ?? $"{exception.GetType().Name}: {exception.Message}");
            logger.LogError("Run {RunId} failed: {Error}", run.Id, error);
            activity?.SetStatus(ActivityStatusCode.Error, error);
            store.Finish(run.Id, "failed", null, error, elapsed.Elapsed.TotalMilliseconds);
        }
        finally
        {
            coordinator.Unregister(run.Id);
            coordinator.Signal();
        }
    }
}
