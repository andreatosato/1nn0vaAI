using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using Microsoft.Extensions.Configuration;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static partial class OfflineSelfTests
{
    private static async Task ValidateAuthenticatedTransport(IShopData data)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var wrongSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var defaults = new AgentTransportAccess(new ConfigurationBuilder().Build());
        Check(!defaults.AllowRemote && defaults.IsAuthorized(IPAddress.Loopback, null), "Default remains unauthenticated loopback-only");
        Check(defaults.IsAuthorized(IPAddress.IPv6Loopback, null)
              && defaults.IsAuthorized(IPAddress.Parse("::ffff:127.0.0.1"), null), "Default accepts IPv6 and mapped loopback");
        Check(!defaults.IsAuthorized(IPAddress.Parse("192.0.2.10"), secret), "A key alone never enables remote access");
        CheckTransportFailure("a2a_loopback_required", () => defaults.ValidateEndpoint(new Uri("http://agent-host:8080")));
        foreach (var badSecret in new[] { "", "short", new string('x', 31) + "\r\n", new string('x', 257) })
        {
            CheckTransportFailure("agent_transport_secret_required", () => new AgentTransportAccess(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agents:AllowRemote"] = "true", ["Agents:SharedSecret"] = badSecret
                }).Build()));
        }

        var remote = new AgentTransportAccess(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agents:AllowRemote"] = "true", ["Agents:SharedSecret"] = secret
        }).Build());
        remote.ValidateEndpoint(new Uri("http://agent-host:8080"));
        Check(remote.IsAuthorized(IPAddress.Parse("192.0.2.10"), secret), "Authenticated non-loopback peers are explicitly allowed");
        Check(!remote.IsAuthorized(IPAddress.Loopback, null) && !remote.IsAuthorized(IPAddress.Loopback, wrongSecret),
            "Remote mode never bypasses authentication for a loopback proxy");
        CheckTransportFailure("a2a_invalid_endpoint", () => remote.ValidateEndpoint(new Uri("http://user:password@agent-host:8080")));
        CheckTransportFailure("a2a_invalid_endpoint", () => remote.ValidateEndpoint(new Uri($"http://agent-host:8080/?key={secret}")));
        CheckTransportFailure("a2a_invalid_endpoint", () => remote.ValidateEndpoint(new Uri($"http://agent-host:8080/{secret}")));
        Check(!remote.Redact("Failure containing " + secret).Contains(secret), "Actual configured key is redacted from transport errors");
        foreach (var access in new[] { defaults, remote })
        {
            foreach (var path in new[] { "/health", "/alive" })
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
                context.Request.Method = HttpMethods.Get;
                context.Request.Path = path;
                var passed = false;
                await AgentHostApplication.EnforceTransportAccessAsync(context, _ =>
                {
                    passed = true;
                    return Task.CompletedTask;
                }, access, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
                Check(passed, "Real middleware admits remote anonymous GET health/alive in both access modes");
            }
        }

        var traceLeaks = 0;
        var tracedActivities = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Observatory.", StringComparison.Ordinal)
                || source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || source.Name.StartsWith("System.Net.Http", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                var text = JsonSerializer.Serialize(new
                {
                    activity.DisplayName,
                    tags = activity.TagObjects.ToArray(),
                    baggage = activity.Baggage.ToArray(),
                    events = activity.Events.Select(item => new { item.Name, tags = item.Tags.ToArray() }).ToArray()
                }, AgentJson.Options);
                if (text.Contains(secret, StringComparison.Ordinal) || text.Contains(wrongSecret, StringComparison.Ordinal))
                    Interlocked.Increment(ref traceLeaks);
                Interlocked.Increment(ref tracedActivities);
            }
        };
        ActivitySource.AddActivityListener(listener);
        await using var services = await SpecialistServices.StartAsync(data,
            "--Agents:AllowRemote", "true",
            "--Agents:SharedSecret", secret,
            "--AllowedHosts", "localhost");
        foreach (var role in AgentNames.Specialists)
        {
            using var anonymous = services.CreateClient(role, authenticate: false);
            using var http = services.CreateClient(role);
            var address = services.Addresses[role];
            foreach (var path in new[] { "/health", "/alive" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Host = $"{role}-service:8080";
                using var response = await anonymous.SendAsync(request);
                Check(response.StatusCode == HttpStatusCode.OK, "Container health probes require no secret and accept service hostnames");
                Check(await response.Content.ReadAsStringAsync() == "Healthy"
                      && response.Content.Headers.ContentType?.MediaType == "text/plain",
                    "Anonymous health response is minimal standard text only");
                foreach (var method in new[] { HttpMethod.Head, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Options })
                    await Reject(anonymous, method, path, null);
            }
            var protectedRoutes = new List<(HttpMethod Method, string Path)>
            {
                (HttpMethod.Get, "/"),
                (HttpMethod.Get, "/telemetry/not-a-run"),
                (HttpMethod.Get, $"/a2a/{role}/.well-known/agent-card.json"),
                (HttpMethod.Post, $"/a2a/{role}"),
                (HttpMethod.Get, $"/skills/shop-{role}/SKILL.md")
            };
            protectedRoutes.AddRange(role switch
            {
                AgentNames.Catalog =>
                [
                    (HttpMethod.Get, "/catalog"), (HttpMethod.Get, "/catalog/query"), (HttpMethod.Get, "/catalog/facets"),
                    (HttpMethod.Get, "/products"), (HttpMethod.Get, "/products/83")
                ],
                AgentNames.Orders =>
                [
                    (HttpMethod.Get, "/orders/ORD-1042"), (HttpMethod.Post, "/return-drafts")
                ],
                _ =>
                [
                    (HttpMethod.Get, "/policies"), (HttpMethod.Post, "/return-assessments"),
                    (HttpMethod.Get, "/skills/shop-returns/references/decision-checklist.md")
                ]
            });
            foreach (var (method, path) in protectedRoutes)
            {
                await Reject(anonymous, method, path, null);
                await Reject(anonymous, method, path, wrongSecret);
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"/a2a/{role}/.well-known/agent-card.json"))
            {
                request.Headers.TryAddWithoutValidation(AgentTransportAccess.HeaderName, [secret, wrongSecret]);
                using var response = await anonymous.SendAsync(request);
                Check(response.StatusCode == HttpStatusCode.Unauthorized, "Ambiguous duplicate authentication headers are rejected");
            }
            foreach (var path in new[] { "/health", "/alive" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path);
                using var response = await http.SendAsync(request);
                Check(response.StatusCode == HttpStatusCode.MethodNotAllowed && response.Content.Headers.Allow.Contains("GET"),
                    "Health routes allow only GET even after authentication");
            }
            var card = await new A2ACardResolver(new Uri(address), http, $"/a2a/{role}/.well-known/agent-card.json").GetAgentCardAsync();
            Check(card.SecuritySchemes?[AgentTransportAccess.SecuritySchemeName] is ApiKeySecurityScheme
                { Name: AgentTransportAccess.HeaderName, KeyLocation: "header" }, "Official card advertises API-key header, not its value");
            Check(card.Security?.Any(requirement => requirement.ContainsKey(AgentTransportAccess.SecuritySchemeName)) == true,
                "Official card requires the declared security scheme");
            Check(!JsonSerializer.Serialize(card, AgentJson.Options).Contains(secret), "Agent card never contains credential value");
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"/a2a/{role}/.well-known/agent-card.json"))
            {
                request.Headers.Host = $"{role}-service:8080";
                using var response = await http.SendAsync(request);
                Check(response.StatusCode == HttpStatusCode.OK, "Authenticated discovery supports Docker service Host header");
            }
        }
        await ValidateServiceRoutes(services, data);
        foreach (var technology in DemoTechnologies.All)
        {
            var history = new List<ChatMessageRecord>();
            var ledgerAgents = new HashSet<string>();
            foreach (var turn in data.Scenarios.Single(item => item.Id == "main-six-turns").Turns)
            {
                var request = Request(technology, turn.Message, new() { ConfirmAction = turn.ConfirmAction }, history);
                var requestStart = services.Requests.Count;
                var (result, events) = await Execute(services.Runtime, request);
                foreach (var fact in turn.ExpectedFacts)
                    Check(result.Answer.Contains(fact, StringComparison.OrdinalIgnoreCase), $"Authenticated {technology} preserves six-turn domain facts");
                foreach (var call in Calls(events)) ledgerAgents.Add(call.Agent);
                ValidateLedger(events, request);
                ValidateServiceTransport(services, events, technology, requestStart, request);
                if (technology == DemoTechnologies.A2A) ValidateRemoteLedger(services, events, request);
                var batches = new List<RemoteTelemetryBatch>();
                foreach (var role in AgentNames.Specialists)
                {
                    using var http = services.CreateClient(role);
                    var remoteBatches = await http.GetFromJsonAsync<RemoteTelemetryBatch[]>($"/telemetry/{request.RunId}", AgentJson.Options);
                    Check(remoteBatches is not null && remoteBatches.All(batch => batch.Completed
                          && batch.Events.All(item => item.Agent == role)), "Authenticated telemetry belongs only to the serving specialist");
                    batches.AddRange(remoteBatches!);
                }
                Check(technology == DemoTechnologies.A2A ? batches.Count > 0 : batches.Count == 0,
                    "Only actual remote A2A execution creates per-service model ledgers");
                var captured = JsonSerializer.Serialize(new { result, events, batches }, AgentJson.Options);
                Check(!captured.Contains(secret, StringComparison.Ordinal), "No credential in HTTP/A2A payload, events, model requests/responses or telemetry");
                Check(!captured.Contains("\"sharedSecret\"", StringComparison.OrdinalIgnoreCase), "Run metadata contains no transport configuration");
                history.Add(new() { Role = "user", Text = turn.Message });
                history.Add(new() { Role = "assistant", Text = result.Answer, ProductIds = result.ProductIds, Sources = result.Sources });
            }
            Check(ledgerAgents.SetEquals(AgentNames.ForTechnology(technology)), $"Authenticated {technology} executes only the actual active agents");
        }

        var failedConfiguration = new ConfigurationBuilder().AddConfiguration(services.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:AllowRemote"] = "true", ["Agents:SharedSecret"] = wrongSecret,
                ["Demo:AllowLive"] = "false"
            }).Build();
        using (var provider = new ServiceCollection().AddObservatoryAgents(failedConfiguration).BuildServiceProvider())
        {
            await ExpectFailure("a2a_discovery_failed", () => Execute(provider.GetRequiredService<IAgentRuntime>(),
                Request("a2a", "Mostra l'ordine ORD-1042.")));
            foreach (var technology in new[] { DemoTechnologies.Inline, DemoTechnologies.Skills })
            {
                var events = new List<RunEvent>();
                try
                {
                    await provider.GetRequiredService<IAgentRuntime>().ExecuteAsync(Request(technology, "Mostra l'ordine ORD-1042."),
                        item => { events.Add(item); return Task.CompletedTask; });
                    throw new InvalidOperationException("Expected authenticated business transport failure.");
                }
                catch (ShopServiceException)
                {
                    Check(!events.Any(item => item.Kind == "answer.delta"), "Wrong HTTP key fails the run without domain-success fallback");
                    Check(!JsonSerializer.Serialize(events, AgentJson.Options).Contains(wrongSecret), "HTTP authentication-failure evidence redacts the supplied key");
                }
            }
        }
        Check(tracedActivities > 0 && traceLeaks == 0, "Native HTTP/agent/model trace tags, baggage and events contain no shared key");
        Console.WriteLine("PASS authenticated specialist services: HTTP/A2A/skills key protection, all three six-turn modes, complete telemetry, no secret leakage.");

        async Task Reject(HttpClient http, HttpMethod method, string path, string? key)
        {
            using var request = new HttpRequestMessage(method, path);
            if (key is not null) request.Headers.Add(AgentTransportAccess.HeaderName, key);
            if (method == HttpMethod.Post)
                request.Content = new StringContent("""{"jsonrpc":"2.0","id":"auth-check","method":"message/send","params":{}}""", Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.Unauthorized, "Missing/wrong key rejected before A2A, business, skills or telemetry handling");
            Check(!body.Contains(secret) && !body.Contains(wrongSecret), "Authentication errors never echo credential values");
        }
    }

    private static void CheckTransportFailure(string code, Action action)
    {
        try { action(); throw new InvalidOperationException("Expected transport configuration/access rejection."); }
        catch (DomainException error) { Check(error.Code == code, $"Expected transport rejection {code}"); }
    }
}
