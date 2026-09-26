using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Observatory.Core;

namespace Observatory.RouterHost;

public sealed class EvidenceStore : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection connection;
    private readonly FileStream ownership;
    private readonly string technology;
    private readonly int maxPending;

    public EvidenceStore(ObservatorySettings settings)
    {
        technology = settings.Technology;
        maxPending = settings.MaxPendingRuns;
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);
        ownership = new FileStream(settings.DatabasePath + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = settings.DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true, DefaultTimeout = 30, Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var setup = Command("""
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS conversations (
                    id TEXT PRIMARY KEY, created_at TEXT NOT NULL, record_json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS runs (
                    ordinal INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL UNIQUE,
                    conversation_id TEXT NOT NULL REFERENCES conversations(id),
                    status TEXT NOT NULL, record_json TEXT NOT NULL, snapshot_json TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_runs_queue ON runs(status, ordinal);
                CREATE INDEX IF NOT EXISTS ix_runs_conversation ON runs(conversation_id, ordinal);
                CREATE TABLE IF NOT EXISTS idempotency (
                    conversation_id TEXT NOT NULL REFERENCES conversations(id), key TEXT NOT NULL,
                    request_hash TEXT NOT NULL, run_id TEXT NOT NULL REFERENCES runs(id),
                    PRIMARY KEY(conversation_id, key));
                CREATE TABLE IF NOT EXISTS events (
                    run_id TEXT NOT NULL REFERENCES runs(id), sequence INTEGER NOT NULL,
                    event_id TEXT NOT NULL UNIQUE, record_json TEXT NOT NULL,
                    PRIMARY KEY(run_id, sequence));
                CREATE TABLE IF NOT EXISTS model_calls (
                    run_id TEXT NOT NULL REFERENCES runs(id), call_id TEXT NOT NULL,
                    record_json TEXT NOT NULL, PRIMARY KEY(run_id, call_id));
                CREATE TABLE IF NOT EXISTS experiments (
                    id TEXT PRIMARY KEY, request_json TEXT NOT NULL, record_json TEXT NOT NULL);
                """);
            setup.ExecuteNonQuery();
            using var existing = Command("SELECT value FROM metadata WHERE key='technology'");
            var storedTechnology = existing.ExecuteScalar() as string;
            if (storedTechnology is not null && storedTechnology != technology)
                throw new InvalidOperationException("Storage:Path belongs to a different demo. Configure a unique database per technology.");
            using var metadata = Command("INSERT OR IGNORE INTO metadata(key,value) VALUES('technology',$value)",
                null, ("$value", technology));
            metadata.ExecuteNonQuery();
            using var schema = Command("SELECT value FROM metadata WHERE key='schema_version'");
            if (schema.ExecuteScalar() is string version && version != "1")
                throw new InvalidOperationException("Unsupported SQLite schema version; do not replace the evidence database.");
            using var versionInsert = Command("INSERT OR IGNORE INTO metadata(key,value) VALUES('schema_version','1')");
            versionInsert.ExecuteNonQuery();
        }
        catch
        {
            connection.Dispose();
            ownership.Dispose();
            throw;
        }
    }

    public ConversationRecord CreateConversation(string? title)
    {
        if (title is { Length: > 160 } || title?.Any(char.IsControl) == true)
            throw new ApiException(400, "invalid_title", "Title must be at most 160 characters without control characters.");
        var record = new ConversationRecord
        {
            Technology = technology,
            Title = string.IsNullOrWhiteSpace(title) ? "Nuova conversazione" : title.Trim()
        };
        lock (gate)
        {
            using var command = Command("INSERT INTO conversations(id,created_at,record_json) VALUES($id,$at,$json)",
                null, ("$id", record.Id), ("$at", record.CreatedAt.ToString("O")), ("$json", ApiJson.Serialize(record)));
            command.ExecuteNonQuery();
        }
        return record;
    }

    public IReadOnlyList<ConversationRecord> Conversations()
    {
        lock (gate)
        {
            using var command = Command("SELECT record_json FROM conversations ORDER BY created_at DESC,id DESC");
            return ReadRecords<ConversationRecord>(command);
        }
    }

    public ConversationRecord GetConversation(string id)
    {
        lock (gate) return ReadConversation(id);
    }

    public SubmissionResult Submit(string conversationId, RunSnapshot snapshot, string? scenarioId = null,
        string? experimentId = null)
    {
        var request = snapshot.Request;
        var hashRequest = request with
        {
            IdempotencyKey = "",
            Configuration = request.Configuration with
            {
                AgentModels = request.Configuration.AgentModels.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.Value)
            }
        };
        var hashPayload = JsonSerializer.SerializeToNode(hashRequest, ApiJson.Options)!;
        // Preserve idempotency hashes written before prompt blocks existed.
        if (hashRequest.Configuration.PromptBlocks == new PromptBlockSelection())
            hashPayload["configuration"]!.AsObject().Remove("promptBlocks");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload.ToJsonString(ApiJson.Options))));
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            var conversation = ReadConversation(conversationId, tx);
            using (var previous = Command(
                "SELECT run_id,request_hash FROM idempotency WHERE conversation_id=$id AND key=$key",
                tx, ("$id", conversationId), ("$key", request.IdempotencyKey)))
            {
                string? previousId = null, previousHash = null;
                using (var reader = previous.ExecuteReader())
                    if (reader.Read()) { previousId = reader.GetString(0); previousHash = reader.GetString(1); }
                if (previousId is not null)
                {
                    if (previousHash != hash)
                        throw new ApiException(409, "idempotency_conflict", "This idempotency key was already used for a different request.");
                    var previousRun = ReadRun(previousId, tx);
                    tx.Commit();
                    return new(previousRun, true);
                }
            }
            using (var count = Command("SELECT COUNT(*) FROM runs WHERE status IN ('queued','running','cancelling')", tx))
                if ((long)count.ExecuteScalar()! >= maxPending)
                    throw new ApiException(429, "queue_full", "The durable queue is full. Retry later with the same idempotency key.");

            var run = new RunRecord
            {
                Id = Guid.NewGuid().ToString("N"), ConversationId = conversationId, Technology = technology,
                Message = request.Message, Configuration = request.Configuration,
                ScenarioId = scenarioId, ExperimentId = experimentId,
                CostStatus = "unpriced"
            };
            using (var insert = Command("""
                INSERT INTO runs(id,conversation_id,status,record_json,snapshot_json) VALUES($id,$conversation,$status,$json,$snapshot);
                INSERT INTO idempotency(conversation_id,key,request_hash,run_id) VALUES($conversation,$key,$hash,$id);
                """, tx, ("$id", run.Id), ("$conversation", conversationId), ("$status", run.Status),
                ("$json", ApiJson.Serialize(run)), ("$snapshot", ApiJson.Serialize(snapshot)),
                ("$key", request.IdempotencyKey), ("$hash", hash)))
                insert.ExecuteNonQuery();
            conversation.Messages.Add(new() { Role = "user", Text = request.Message, RunId = run.Id });
            SaveConversation(conversation, tx);
            WriteEvent(new()
            {
                RunId = run.Id, Kind = "run.queued", Message = "Run persisted and queued.",
                Data = new { status = "queued", mode = run.Configuration.Mode, technology, originalRunId = run.Id }
            }, tx);
            tx.Commit();
            return new(run, false);
        }
    }

    public IReadOnlyList<RunRecord> Runs()
    {
        lock (gate)
        {
            using var command = Command("SELECT record_json FROM runs ORDER BY ordinal DESC");
            return ReadRecords<RunRecord>(command).Select(run => run with { Calls = ReadCalls(run.Id) }).ToArray();
        }
    }

    public int ClearRunHistory()
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            using (var activeRuns = Command(
                "SELECT COUNT(*) FROM runs WHERE status IN ('queued','running','cancelling')", tx))
            {
                if ((long)activeRuns.ExecuteScalar()! > 0)
                    throw new ApiException(409, "runs_active", "Run attivi: completa o annulla le esecuzioni prima di cancellare lo storico.");
            }

            List<ExperimentResult> experiments;
            using (var command = Command("SELECT record_json FROM experiments", tx))
                experiments = ReadRecords<ExperimentResult>(command);
            if (experiments.Any(experiment => experiment.Status == "running"))
                throw new ApiException(409, "experiments_active", "Esperimento attivo: attendi il completamento prima di cancellare lo storico.");

            List<ConversationRecord> conversations;
            using (var command = Command("SELECT record_json FROM conversations", tx))
                conversations = ReadRecords<ConversationRecord>(command);
            foreach (var conversation in conversations)
            {
                var messages = conversation.Messages
                    .Select(message => message.RunId is null ? message : message with { RunId = null })
                    .ToList();
                SaveConversation(conversation with { Messages = messages }, tx);
            }

            using var count = Command("SELECT COUNT(*) FROM runs", tx);
            var deletedRuns = checked((int)(long)count.ExecuteScalar()!);
            using (var command = Command("""
                DELETE FROM idempotency;
                DELETE FROM events;
                DELETE FROM model_calls;
                DELETE FROM runs;
                DELETE FROM experiments;
                """, tx))
                command.ExecuteNonQuery();
            tx.Commit();
            return deletedRuns;
        }
    }

    public RunRecord GetRun(string id, bool details = true)
    {
        lock (gate)
        {
            var run = ReadRun(id);
            return details ? run with { Events = ReadEvents(id, 0), Calls = ReadCalls(id) } : run;
        }
    }

    public RunSnapshot GetSnapshot(string id)
    {
        lock (gate)
        {
            using var command = Command("SELECT snapshot_json FROM runs WHERE id=$id", null, ("$id", id));
            return command.ExecuteScalar() is string json ? ApiJson.Deserialize<RunSnapshot>(json)
                : throw NotFound("Run");
        }
    }

    public RunRecord? ClaimNext()
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            using var command = Command("""
                SELECT r.id FROM runs r WHERE r.status='queued'
                  AND NOT EXISTS (SELECT 1 FROM runs earlier
                      WHERE earlier.conversation_id=r.conversation_id AND earlier.ordinal<r.ordinal
                      AND earlier.status IN ('queued','running','cancelling'))
                ORDER BY r.ordinal LIMIT 1
                """, tx);
            if (command.ExecuteScalar() is not string id) return null;
            var run = ReadRun(id, tx) with { Status = "running", StartedAt = DateTimeOffset.UtcNow };
            SaveRun(run, tx);
            WriteEvent(new() { RunId = id, Kind = "run.started", Message = "Execution started.", Data = new { status = "running" } }, tx);
            tx.Commit();
            return run;
        }
    }

    public IReadOnlyList<ChatMessageRecord> HistoryBefore(RunRecord run)
    {
        lock (gate)
        {
            var conversation = ReadConversation(run.ConversationId);
            return conversation.Messages.TakeWhile(m => m.RunId != run.Id).ToArray();
        }
    }

    public RunEvent AppendEvent(RunEvent value, double? elapsedAtReceiptMs = null)
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            var run = ReadRun(value.RunId, tx);
            if (IsTerminal(run.Status))
                throw new InvalidOperationException("An event cannot be appended after a run is terminal.");
            if (value.Kind == "model.completed")
            {
                var call = value.Data switch
                {
                    ModelCallRecord record => record,
                    System.Text.Json.JsonElement element => element.Deserialize<ModelCallRecord>(ApiJson.Options)
                        ?? throw new InvalidDataException("model.completed has null data."),
                    JsonNode node => node.Deserialize<ModelCallRecord>(ApiJson.Options)
                        ?? throw new InvalidDataException("model.completed has null data."),
                    _ => throw new InvalidDataException("model.completed must contain a ModelCallRecord.")
                };
                if (call.RunId != run.Id || call.Mode != run.Configuration.Mode)
                    throw new InvalidDataException("Model call run/mode does not match its run.");
                if (string.IsNullOrWhiteSpace(call.Id))
                    throw new InvalidDataException("Model calls require stable IDs for ledger deduplication.");
                using var snapshotCommand = Command("SELECT snapshot_json FROM runs WHERE id=$id", tx, ("$id", run.Id));
                var snapshot = ApiJson.Deserialize<RunSnapshot>((string)snapshotCommand.ExecuteScalar()!);
                var model = snapshot.Models.SingleOrDefault(m => m.Id == call.ModelProfileId)
                    ?? throw new InvalidDataException("A model call references an unknown model profile.");
                call = CostCalculator.Price(call, model.Pricing);
                using var insert = Command("""
                    INSERT INTO model_calls(run_id,call_id,record_json) VALUES($run,$call,$json)
                    ON CONFLICT(run_id,call_id) DO NOTHING
                    """, tx, ("$run", run.Id), ("$call", call.Id), ("$json", ApiJson.Serialize(call)));
                if (insert.ExecuteNonQuery() == 0)
                {
                    using var old = Command("SELECT record_json FROM model_calls WHERE run_id=$run AND call_id=$call",
                        tx, ("$run", run.Id), ("$call", call.Id));
                    if ((string)old.ExecuteScalar()! != ApiJson.Serialize(call))
                        throw new InvalidDataException("Conflicting model-call records share an ID.");
                }
                value = value with { Data = call };
                run = CostCalculator.Aggregate(run, ReadCalls(run.Id, tx));
            }
            if (value.Kind == "answer.delta" && run.TimeToFirstAnswerMs is null)
                run = run with
                {
                    TimeToFirstAnswerMs = Math.Max(0,
                        elapsedAtReceiptMs ?? (DateTimeOffset.UtcNow - run.StartedAt).TotalMilliseconds)
                };
            SaveRun(run, tx);
            var stored = WriteEvent(value, tx);
            tx.Commit();
            return stored;
        }
    }

    public RunRecord Cancel(string id)
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            var run = ReadRun(id, tx);
            if (IsTerminal(run.Status) || run.Status == "cancelling") { tx.Commit(); return run; }
            var queued = run.Status == "queued";
            run = run with
            {
                Status = queued ? "cancelled" : "cancelling",
                CompletedAt = queued ? DateTimeOffset.UtcNow : null,
                Error = queued ? "Cancelled before execution." : null
            };
            SaveRun(run, tx);
            WriteEvent(new()
            {
                RunId = id, Kind = queued ? "run.cancelled" : "run.cancelling",
                Message = queued ? "Cancelled before execution; no model was called." : "Cancellation requested.",
                Data = new { status = run.Status }
            }, tx);
            tx.Commit();
            return run;
        }
    }

    public RunRecord Finish(string id, string status, AgentExecutionResult? result, string? error, double durationMs)
    {
        if (status is not ("completed" or "failed" or "cancelled"))
            throw new ArgumentOutOfRangeException(nameof(status));
        lock (gate)
        {
            using var tx = connection.BeginTransaction(deferred: false);
            var run = ReadRun(id, tx);
            if (IsTerminal(run.Status)) { tx.Commit(); return run; }
            if (run.Status == "cancelling") { status = "cancelled"; result = null; error = "Cancelled by user."; }
            run = CostCalculator.Aggregate(run with
            {
                Status = status, Result = status == "completed" ? result : null, Error = error,
                CompletedAt = DateTimeOffset.UtcNow, DurationMs = durationMs
            }, ReadCalls(id, tx));
            SaveRun(run, tx);
            if (status == "completed" && result is not null)
            {
                var conversation = ReadConversation(run.ConversationId, tx);
                var index = conversation.Messages.FindIndex(m => m.RunId == id && m.Role == "user");
                if (index < 0) throw new InvalidDataException("The run has no persisted user message.");
                conversation.Messages.Insert(index + 1, new()
                {
                    Role = "assistant", Text = result.Answer, RunId = id,
                    ProductIds = result.ProductIds, Sources = result.Sources
                });
                SaveConversation(conversation, tx);
            }
            WriteEvent(new()
            {
                RunId = id, Kind = "run." + status, Message = error ?? "Run completed.",
                Data = new { status, result = run.Result, run.EstimatedCostUsd, run.CostStatus, error }
            }, tx);
            tx.Commit();
            return run;
        }
    }

    public IReadOnlyList<RunEvent> EventsAfter(string id, long after, int limit = 256)
    {
        lock (gate)
        {
            _ = ReadRun(id);
            return ReadEvents(id, after, limit: limit);
        }
    }

    public long ResolveEventCursor(string id, string? cursor)
    {
        lock (gate)
        {
            _ = ReadRun(id);
            if (string.IsNullOrWhiteSpace(cursor)) return 0;
            if (long.TryParse(cursor, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var sequence) && sequence >= 0)
            {
                using var max = Command("SELECT COALESCE(MAX(sequence),0) FROM events WHERE run_id=$id",
                    null, ("$id", id));
                if (sequence <= (long)max.ExecuteScalar()!) return sequence;
                throw new ApiException(400, "invalid_event_cursor", "Last-Event-ID is beyond this run's stored events.");
            }
            using var command = Command("SELECT sequence FROM events WHERE run_id=$run AND event_id=$event",
                null, ("$run", id), ("$event", cursor));
            return command.ExecuteScalar() is long found ? found
                : throw new ApiException(400, "invalid_event_cursor", "Last-Event-ID does not belong to this run.");
        }
    }

    public void RecoverInterrupted()
    {
        lock (gate)
        {
            using var command = Command("SELECT record_json FROM runs WHERE status IN ('running','cancelling','queued')");
            var unfinished = ReadRecords<RunRecord>(command);
            foreach (var run in unfinished)
            {
                Finish(run.Id, "failed", null,
                    "Execution interrupted by process restart. Stored events remain replayable; models are never re-executed automatically.",
                    Math.Max(0, (DateTimeOffset.UtcNow - run.StartedAt).TotalMilliseconds));
            }
            using var experiments = Command("SELECT id,record_json FROM experiments");
            var interrupted = new List<ExperimentResult>();
            using (var reader = experiments.ExecuteReader())
                while (reader.Read())
                {
                    var result = ApiJson.Deserialize<ExperimentResult>(reader.GetString(1));
                    if (result.Status == "running") interrupted.Add(result);
                }
            foreach (var result in interrupted)
                SaveExperiment(result.Id, null, result with
                {
                    Status = "interrupted", CompletedAt = DateTimeOffset.UtcNow,
                    Notice = "Process restarted; completed per-case evidence is preserved. This experiment was not automatically restarted."
                });
        }
    }

    public void SaveExperiment(string id, ExperimentRequest? request, ExperimentResult result)
    {
        lock (gate)
        {
            using var command = request is null
                ? Command("UPDATE experiments SET record_json=$json WHERE id=$id", null,
                    ("$id", id), ("$json", ApiJson.Serialize(result)))
                : Command("""
                    INSERT INTO experiments(id,request_json,record_json) VALUES($id,$request,$json)
                    ON CONFLICT(id) DO UPDATE SET record_json=excluded.record_json
                    """, null, ("$id", id), ("$request", ApiJson.Serialize(request)), ("$json", ApiJson.Serialize(result)));
            command.ExecuteNonQuery();
        }
    }

    public ExperimentResult GetExperiment(string id)
    {
        lock (gate)
        {
            using var command = Command("SELECT record_json FROM experiments WHERE id=$id", null, ("$id", id));
            return command.ExecuteScalar() is string json ? ApiJson.Deserialize<ExperimentResult>(json) : throw NotFound("Experiment");
        }
    }

    public static bool IsTerminal(string status) => status is "completed" or "failed" or "cancelled";

    private ConversationRecord ReadConversation(string id, SqliteTransaction? tx = null)
    {
        using var command = Command("SELECT record_json FROM conversations WHERE id=$id", tx, ("$id", id));
        return command.ExecuteScalar() is string json ? ApiJson.Deserialize<ConversationRecord>(json) : throw NotFound("Conversation");
    }

    private RunRecord ReadRun(string id, SqliteTransaction? tx = null)
    {
        using var command = Command("SELECT record_json FROM runs WHERE id=$id", tx, ("$id", id));
        return command.ExecuteScalar() is string json ? ApiJson.Deserialize<RunRecord>(json) : throw NotFound("Run");
    }

    private List<ModelCallRecord> ReadCalls(string id, SqliteTransaction? tx = null)
    {
        using var command = Command("SELECT record_json FROM model_calls WHERE run_id=$id ORDER BY rowid", tx, ("$id", id));
        return ReadRecords<ModelCallRecord>(command);
    }

    private List<RunEvent> ReadEvents(string id, long after, SqliteTransaction? tx = null, int limit = int.MaxValue)
    {
        using var command = Command("""
            SELECT record_json FROM events WHERE run_id=$id AND sequence>$after ORDER BY sequence LIMIT $limit
            """, tx, ("$id", id), ("$after", after), ("$limit", limit));
        return ReadRecords<RunEvent>(command);
    }

    private RunEvent WriteEvent(RunEvent value, SqliteTransaction tx)
    {
        using var max = Command("SELECT COALESCE(MAX(sequence),0)+1 FROM events WHERE run_id=$id", tx, ("$id", value.RunId));
        var record = value with { Id = Guid.NewGuid().ToString("N"), Sequence = (long)max.ExecuteScalar()! };
        using var command = Command("INSERT INTO events(run_id,sequence,event_id,record_json) VALUES($run,$seq,$id,$json)",
            tx, ("$run", record.RunId), ("$seq", record.Sequence), ("$id", record.Id), ("$json", ApiJson.Serialize(record)));
        command.ExecuteNonQuery();
        return record;
    }

    private void SaveRun(RunRecord run, SqliteTransaction tx)
    {
        using var command = Command("UPDATE runs SET status=$status,record_json=$json WHERE id=$id", tx,
            ("$status", run.Status), ("$json", ApiJson.Serialize(run with { Events = [], Calls = [] })), ("$id", run.Id));
        command.ExecuteNonQuery();
    }

    private void SaveConversation(ConversationRecord conversation, SqliteTransaction tx)
    {
        using var command = Command("UPDATE conversations SET record_json=$json WHERE id=$id", tx,
            ("$json", ApiJson.Serialize(conversation)), ("$id", conversation.Id));
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string text, SqliteTransaction? tx = null, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = text;
        command.Transaction = tx;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static List<T> ReadRecords<T>(SqliteCommand command)
    {
        var result = new List<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ApiJson.Deserialize<T>(reader.GetString(0)));
        return result;
    }

    private static ApiException NotFound(string resource) => new(404, "not_found", $"{resource} not found.");

    public void Dispose()
    {
        lock (gate)
        {
            connection.Dispose();
            ownership.Dispose();
        }
    }
}
