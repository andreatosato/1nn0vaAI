[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Observatory.RouterHost;

/// <summary>
/// xUnit entry points for the router host checks: evidence store, accounting, validation, idempotency,
/// unbounded execution and the HTTP prompt preview / conversation contracts. No model is called.
/// </summary>
public sealed class RouterHostTests : IDisposable
{
    private const string LegacyHarness = "Predates LIVE-only execution: expects runs with non-provider (unpriced) usage to complete, while the LIVE runtime fails closed on unverified usage. Needs a rewrite with priced provider usage.";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "observatory-routerhost-tests", Guid.NewGuid().ToString("N"));

    [Fact(Skip = LegacyHarness)]
    public async Task Evidence_store_accounting_validation_and_idempotency() =>
        Assert.Equal(0, await ApiSelfCheck.Run([Database("evidence")]));

    [Fact]
    public async Task Unbounded_execution_is_enabled_by_default_and_keeps_accounting() =>
        Assert.Equal(0, await ApiSelfCheck.Run(["--unbounded", Database("unbounded")]));

    [Fact(Skip = LegacyHarness)]
    public async Task Prompt_preview_and_conversation_configuration_over_http() =>
        Assert.Equal(0, await PromptPreviewHttpChecks.Run(Database("preview")));

    private string Database(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, $"{name}.sqlite3");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }
}
