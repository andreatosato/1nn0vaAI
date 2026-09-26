using A2A;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.SpecialistHost;

namespace Observatory.Agents.Tests;

/// <summary>
/// Router → specialist over the official A2A SDK, in memory: card discovery, message/send, evidence returned in the
/// reply metadata (so the router ledger includes the specialist's model calls and costs) and domain errors.
/// </summary>
public sealed class SpecialistA2ATests : IAsyncLifetime
{
    private WebApplication _specialist = null!;
    private SpecialistClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = SpecialistHostApplication.CreateBuilder<FakeCatalogAgent>([]);
        // In Aspire, service discovery sends Host=localhost:port; the in-memory server keeps the logical name.
        builder.Configuration["AllowedHosts"] = "*";
        builder.WebHost.UseTestServer();
        _specialist = SpecialistHostApplication.Build(builder);
        await _specialist.StartAsync();
        _client = new SpecialistClient(new TestServerHttpClientFactory(_specialist.GetTestServer()));
    }

    public async Task DisposeAsync() => await _specialist.DisposeAsync();

    [Fact]
    public async Task Agent_card_is_published_at_the_well_known_path()
    {
        using var http = new HttpClient(_specialist.GetTestServer().CreateHandler());

        using var response = await http.GetAsync("http://agent-catalog/a2a/catalog/.well-known/agent-card.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.Contains("\"url\":\"http://agent-catalog/a2a/catalog\"", body);
    }

    [Fact]
    public async Task Answer_reaches_the_router_and_specialist_evidence_is_imported_into_its_ledger()
    {
        var events = new List<RunEvent>();
        var state = new RunState(Request(), item => { events.Add(item); return Task.CompletedTask; });

        var answer = await _client.InvokeAsync(state, AgentNames.Catalog, "camicie sotto 40 USD", CancellationToken.None);

        Assert.Equal(FakeCatalogAgent.Answer, answer);
        var call = Assert.Single(events, item => item.Kind == "model.completed");
        Assert.Equal(AgentNames.Catalog, call.Agent);
        Assert.Equal(4, state.RemainingCalls);
        Assert.Equal(0.99m, state.RemainingBudget);
        Assert.Contains(86, state.Result("").ProductIds);
        Assert.Contains(events, item => item.Kind == "protocol.request" && item.Message.Contains("agent-card"));
        Assert.Contains(events, item => item.Kind == "protocol.request" && item.Message.Contains("message/send"));
        Assert.Equal(events.Select(item => item.Sequence), events.Select(item => item.Sequence).Order());
    }

    [Fact]
    public async Task Domain_errors_keep_their_code_and_still_account_for_the_specialist_model_call()
    {
        var events = new List<RunEvent>();
        var state = new RunState(Request(), item => { events.Add(item); return Task.CompletedTask; });

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _client.InvokeAsync(state, AgentNames.Catalog, FakeCatalogAgent.FailingQuery, CancellationToken.None));

        Assert.Equal("product_not_found", error.Code);
        Assert.Single(events, item => item.Kind == "model.completed");
        Assert.Equal(4, state.RemainingCalls);
    }

    [Fact]
    public async Task Specialists_reject_runs_of_other_architectures()
    {
        var state = new RunState(Request() with { Technology = DemoTechnologies.Inline }, _ => Task.CompletedTask);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _client.InvokeAsync(state, AgentNames.Catalog, "ciao", CancellationToken.None));

        Assert.True(error.Code == "a2a_transport_failed", $"{error.Code}: {error.Message}");
    }

    [Fact]
    public async Task Each_specialist_reports_its_own_instructions()
    {
        var preview = await _client.PreviewAsync(AgentNames.Catalog, new RunConfiguration(), CancellationToken.None);

        Assert.Equal(AgentNames.Catalog, preview.Agent);
        Assert.Equal(FakeCatalogAgent.Prompt, preview.Instructions);
    }

    private static AgentRunRequest Request() => new()
    {
        RunId = "run-a2a", ConversationId = "conversation-a2a", Technology = DemoTechnologies.A2A, Message = "ciao",
        Configuration = new RunConfiguration { MaxModelCalls = 5, ApprovedBudgetUsd = 1m }
    };

    private sealed class FakeCatalogAgent : ISpecialistAgent
    {
        public const string Answer = "Camicia 86 a 19.99 USD.";
        public const string FailingQuery = "prodotto inesistente";
        public const string Prompt = "Istruzioni del catalogo di prova.";

        public string Role => AgentNames.Catalog;
        public string CardDescription => "Catalogo di prova.";
        public string SkillDescription => "Ricerca di prova.";
        public string Instructions(AgentRunRequest request) => Prompt;
        public IList<AITool> Tools(RunState state) => [];

        public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, string query,
            Func<RunEvent, Task> emit, CancellationToken cancellationToken)
        {
            await emit(new RunEvent
            {
                RunId = request.RunId, Kind = "model.completed", Agent = Role,
                Data = new ModelCallRecord
                {
                    RunId = request.RunId, Agent = Role, ModelProfileId = "gpt5", UsageSource = "provider",
                    InputTokens = 100, OutputTokens = 10, EstimatedCostUsd = 0.01m, CostStatus = "priced"
                }
            });
            if (query == FailingQuery) throw new DomainException("product_not_found", "Prodotto non trovato.");
            return new AgentExecutionResult(Answer, [86], ["catalog:DummyJSON"]);
        }
    }

    private sealed class TestServerHttpClientFactory(TestServer server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(server.CreateHandler());
    }
}
