using System.Collections.Concurrent;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal sealed class RemoteTelemetryStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _capacityLock = new();
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    public Task<AgentExecutionResult> ExecuteAsync(string runId, string invocationId, string fingerprint,
        Func<Func<RunEvent, Task>, Task<AgentExecutionResult>> execute)
    {
        Entry entry;
        lock (_capacityLock)
        {
            var key = $"{runId}:{invocationId}";
            if (!_entries.TryGetValue(key, out entry!))
            {
                Prune();
                if (_entries.Count >= 512)
                    throw new DomainException("host_capacity", "Capacità della telemetria interna raggiunta; nessuna chiamata eseguita.");
                entry = new(runId, invocationId, fingerprint, execute);
                _entries.TryAdd(key, entry);
            }
            if (entry.Fingerprint != fingerprint)
                throw new DomainException("invocation_conflict", "InvocationId già usato con dati o configurazione diversi.");
        }
        return entry.Execution.Value;
    }

    public RemoteTelemetryBatch? Read(string runId, string invocationId) =>
        _entries.TryGetValue($"{runId}:{invocationId}", out var entry) ? entry.Snapshot() : null;

    public IReadOnlyList<RemoteTelemetryBatch> ReadRun(string runId) =>
        _entries.Values.Where(entry => entry.RunId == runId).Select(entry => entry.Snapshot()).ToArray();

    private void Prune()
    {
        var oldest = DateTimeOffset.UtcNow - Retention;
        foreach (var (key, entry) in _entries)
            if (entry.Completed && entry.CreatedAt < oldest) _entries.TryRemove(key, out _);
    }

    private sealed class Entry
    {
        private readonly object _lock = new();
        private readonly List<RunEvent> _events = [];
        private bool _completed;
        public string RunId { get; }
        public string InvocationId { get; }
        public string Fingerprint { get; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public bool Completed { get { lock (_lock) return _completed; } }
        public Lazy<Task<AgentExecutionResult>> Execution { get; }

        public Entry(string runId, string invocationId, string fingerprint,
            Func<Func<RunEvent, Task>, Task<AgentExecutionResult>> execute)
        {
            RunId = runId;
            InvocationId = invocationId;
            Fingerprint = fingerprint;
            Execution = new(async () =>
            {
                try
                {
                    return await execute(item =>
                    {
                        lock (_lock) _events.Add(item);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                }
                finally
                {
                    lock (_lock) _completed = true;
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public RemoteTelemetryBatch Snapshot()
        {
            lock (_lock) return new(RunId, InvocationId, _completed, _events.ToArray());
        }
    }
}
