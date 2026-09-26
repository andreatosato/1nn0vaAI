using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.RouterHost;

/// <summary>The real router host composition, in memory, with a probe router and a stub Catalog API.</summary>
public sealed class RouterHostHttpTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "observatory-routerhost-http", Guid.NewGuid().ToString("N"));

    public static TheoryData<string> Technologies => new() { DemoTechnologies.Inline, DemoTechnologies.Skills, DemoTechnologies.A2A };

    [Theory]
    [MemberData(nameof(Technologies))]
    public async Task Config_describes_the_architecture_declared_by_the_router(string technology)
    {
        await using var app = await Start(technology);

        var config = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/api/config");

        var architecture = TestArchitectures.For(technology);
        Assert.Equal(technology, config.GetProperty("technology").GetString());
        var capabilities = config.GetProperty("capabilities");
        Assert.Equal(architecture.Agents, capabilities.GetProperty("agentNames").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(architecture.Topology, capabilities.GetProperty("executionTopology").GetString());
    }

    [Theory]
    [MemberData(nameof(Technologies))]
    public async Task Prompt_preview_returns_the_instructions_reported_by_the_router(string technology)
    {
        await using var app = await Start(technology);
        var configuration = new RunConfiguration { PromptBlocks = new() { Checklist = true } };

        using var response = await app.GetTestClient().PostAsync("/api/prompts/preview",
            new StringContent(ApiJson.Serialize(configuration), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = JsonSerializer.SerializeToElement(TestArchitectures.Preview(technology, configuration), ApiJson.Options);
        var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(JsonElement.DeepEquals(expected, actual), actual.GetRawText());
    }

    [Fact]
    public async Task Prompt_preview_rejects_agents_of_other_architectures()
    {
        await using var app = await Start(DemoTechnologies.Inline);

        using var response = await app.GetTestClient().PostAsync("/api/prompts/preview",
            new StringContent("""{"agentModels":{"catalog":"gpt5"}}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_agent_name", await response.Content.ReadAsStringAsync());
    }

    private async Task<WebApplication> Start(string technology)
    {
        var builder = RouterHostApplication.CreateBuilder<PreviewRouter>([], TestArchitectures.For(technology));
        builder.WebHost.UseTestServer();
        builder.Configuration["Storage:Path"] = Path.Combine(_directory, technology, "observatory.sqlite3");
        builder.Services.AddHttpClient(ShopServiceClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new CatalogStub());
        var app = await RouterHostApplication.BuildAsync(builder);
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Probe router resolved from DI; it previews but never runs.</summary>
    private sealed class PreviewRouter(DemoArchitecture architecture) : IArchitectureRouter
    {
        private readonly ProbeRouter _probe = new(architecture, null!);
        public DemoArchitecture Architecture => architecture;
        public string Instructions(AgentRunRequest request) => _probe.Instructions(request);
        public IList<Microsoft.Extensions.AI.AITool> Tools(RunState state) => [];
        public Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Preview-only router.");
        public Task<IReadOnlyList<AgentPromptPreview>> PreviewAsync(RunConfiguration configuration, CancellationToken cancellationToken) =>
            _probe.PreviewAsync(configuration, cancellationToken);
    }

    /// <summary>Stands in for the Catalog business API at startup: one product with provenance.</summary>
    private sealed class CatalogStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath != "/catalog") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var snapshot = new CatalogSnapshot
            {
                Source = "stub", SourceUrl = "https://example.invalid/catalog", ContentHash = "stub-hash",
                Products = [new() { Id = 83, Title = "Stub product", Price = 29.99m, Stock = 1 }]
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot, options: AgentJson.Options) });
        }
    }
}
