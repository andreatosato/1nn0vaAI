using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static partial class OfflineSelfTests
{
    private static IReadOnlySet<string> ValidateServiceTransport(SpecialistServices services, List<RunEvent> events,
        string technology, int requestStart = 0, AgentRunRequest? request = null)
    {
        var contacted = new HashSet<string>(StringComparer.Ordinal);
        var actualRequests = services.Requests.Skip(requestStart).ToArray();
        var protocolRequests = events.Where(item => item.Kind == "protocol.request").ToArray();
        Check(protocolRequests.Length > 0 && actualRequests.Length > 0, $"{technology} actually crossed a Kestrel service boundary");
        foreach (var item in protocolRequests)
        {
            var payload = JsonSerializer.SerializeToElement(item.Data, AgentJson.Options);
            var protocol = payload.GetProperty("protocol").GetString();
            var url = new Uri(payload.GetProperty("url").GetString()!);
            if (technology == DemoTechnologies.A2A)
            {
                Check(protocol is "A2A" or "internal-loopback-telemetry" or "internal-authenticated-telemetry",
                    "A2A uses remote specialist agents and a separate telemetry channel, not router business HTTP tools");
                Check(AgentNames.Specialists.Contains(item.Agent), "A2A protocol events identify the actual specialist");
                Check(url.GetLeftPart(UriPartial.Authority) == services.Addresses[item.Agent],
                    $"{item.Agent} A2A event uses its own service origin");
                if (protocol == "A2A")
                {
                    Check(url.AbsolutePath.StartsWith($"/a2a/{item.Agent}", StringComparison.Ordinal),
                        "Official A2A discovery and invocation target the selected role");
                    Check(actualRequests.Any(actual => actual.Role == item.Agent && actual.Path == url.AbsolutePath),
                        "A2A protocol evidence corresponds to an actual request observed by that Kestrel host");
                    contacted.Add(item.Agent);
                }
            }
            else
            {
                var service = payload.GetProperty("service").GetString()!;
                var operation = payload.GetProperty("operation").GetString()!;
                Check(item.Agent == AgentNames.Router && protocol == "HTTP" && AgentNames.Specialists.Contains(service),
                    $"{technology} domain HTTP is attributed to the single router and correct service");
                Check(url.GetLeftPart(UriPartial.Authority) == services.Addresses[service],
                    $"{technology} records the real {service} business origin");
                var (method, path) = operation switch
                {
                    "search_products" => ("GET", "/products"),
                    "query_catalog" => ("GET", "/catalog/query"),
                    "get_catalog_facets" => ("GET", "/catalog/facets"),
                    "get_product" => ("GET", "/products/"),
                    "get_order" => ("GET", "/orders/"),
                    "create_return_draft" => ("POST", "/return-drafts"),
                    "assess_return" => ("POST", "/return-assessments"),
                    "get_policies" => ("GET", "/policies"),
                    _ => throw new InvalidOperationException("Unexpected business operation: " + operation)
                };
                Check(actualRequests.Any(actual => actual.Role == service && actual.Method == method
                      && (path.EndsWith('/') ? actual.Path.StartsWith(path, StringComparison.Ordinal) : actual.Path == path)),
                    $"{technology}/{operation} executed the real {method} {path} endpoint, not a local fallback");
                Check(events.Any(response => response.Kind == "protocol.response" && response.Agent == AgentNames.Router
                    && JsonSerializer.SerializeToElement(response.Data, AgentJson.Options) is var body
                    && body.GetProperty("protocol").GetString() == "HTTP"
                    && body.GetProperty("service").GetString() == service
                    && body.GetProperty("operation").GetString() == operation
                    && new Uri(body.GetProperty("url").GetString()!).GetLeftPart(UriPartial.Authority) == services.Addresses[service]),
                    "Every business HTTP request has matching router/service/origin response evidence");
                contacted.Add(service);
            }
        }
        if (technology == DemoTechnologies.A2A)
        {
            Check(!actualRequests.Any(actual => IsBusinessPath(actual.Path)), "A2A specialists use their own local authoritative shop tools");
            Check(events.Any(item => item.Kind == "tool.called" && item.Agent == AgentNames.Router
                  && item.Message.EndsWith("_agent", StringComparison.Ordinal)), "A2A router uses genuine remote delegation tools");
        }
        else
        {
            Check(actualRequests.All(actual => !actual.Path.StartsWith("/a2a/", StringComparison.Ordinal)
                  && !actual.Path.StartsWith("/telemetry/", StringComparison.Ordinal)),
                $"{technology} never sends A2A or remote-ledger requests");
            Check(!events.Any(item => item.Kind == "tool.called" && item.Message.EndsWith("_agent", StringComparison.Ordinal)),
                $"{technology} never calls fake specialist delegation tools");
            Check(events.Where(item => item.Kind is "agent.started" or "model.completed" or "tool.called")
                    .All(item => item.Agent == AgentNames.Router),
                $"{technology} has only the actual router agent/model/tool events");
            if (request is not null)
                foreach (var actual in actualRequests.Where(actual => actual.Path.StartsWith("/orders/", StringComparison.Ordinal)
                             || actual.Path is "/return-drafts" or "/return-assessments"))
                {
                    Check(actual.Customer == request.CustomerId, "Private HTTP operation receives only this run's trusted customer header");
                    Check(actual.Path == "/return-drafts"
                            ? request.Configuration.ConfirmAction && actual.Confirmation == "true"
                            : actual.Confirmation.Length == 0,
                        "Consent is sent only for an explicitly authorized draft operation, never as leaked client state");
                }
        }
        return contacted;
    }

    private static bool IsBusinessPath(string path) => path is "/catalog" or "/catalog/query" or "/catalog/facets" or "/products" or "/policies"
        or "/return-drafts" or "/return-assessments" || path.StartsWith("/products/", StringComparison.Ordinal)
        || path.StartsWith("/orders/", StringComparison.Ordinal);

    private static async Task ValidateRequiredHostRole(IShopData data)
    {
        foreach (var role in new[] { "", AgentNames.Router, "invented" })
        {
            try
            {
                await using var unexpected = AgentHostApplication.Build([$"--Agents:Role={role}"], data, selfTest: true);
                throw new InvalidOperationException("Expected missing/invalid service role to fail startup.");
            }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("Agents:Role", StringComparison.Ordinal),
                    "A specialist host cannot start without an explicit supported service role");
            }
        }
    }

    private static void ValidateActiveAgents()
    {
        var registry = new AgentModelRegistry(new ConfigurationBuilder().Build());
        foreach (var technology in DemoTechnologies.All)
        {
            var expected = technology == DemoTechnologies.A2A ? AgentNames.All : [AgentNames.Router];
            Check(AgentNames.ForTechnology(technology).SequenceEqual(expected), $"{technology} exposes exactly its active model-bearing agents");
            registry.Validate(Request(technology, "Mostra ORD-1042.", new()
            {
                AgentModels = expected.ToDictionary(agent => agent, _ => "gpt6-luna", StringComparer.Ordinal)
            }));
            CheckValidation(registry, Request(technology, "Mostra ORD-1042.", new()
            {
                AgentModels = new() { ["invented"] = "gpt5" }
            }), "unknown_agent");
            if (technology != DemoTechnologies.A2A)
                foreach (var inactive in AgentNames.Specialists)
                    CheckValidation(registry, Request(technology, "Mostra ORD-1042.", new()
                    {
                        AgentModels = new() { [inactive] = "gpt5" }
                    }), "unknown_agent");
        }
        foreach (var (role, port) in new[] { (AgentNames.Catalog, 5205), (AgentNames.Orders, 5206), (AgentNames.Returns, 5207) })
            Check(registry.ServiceEndpoint(role) == new Uri($"http://localhost:{port}"),
                $"{role} default endpoint uses its dedicated startup port");
    }

    private static async Task ValidateModelOverrides(IAgentRuntime runtime, string technology)
    {
        var request = Request(technology, "ORD-1042 ha un difetto, qual è il rimborso?", new()
        {
            ModelProfileId = "gpt5",
            AgentModels = AgentNames.ForTechnology(technology).ToDictionary(agent => agent,
                agent => agent == AgentNames.Router ? "gpt6-astra" : "gpt6-luna", StringComparer.Ordinal)
        });
        var (result, events) = await Execute(runtime, request);
        Check(result.Decision == "allowed" && result.Answer.Contains("19.99"), $"{technology} active model overrides preserve business authority");
        Check(Calls(events).All(call => call.ModelProfileId == request.Configuration.AgentModels[call.Agent]),
            $"{technology} applies overrides only to real model calls on active agents");
        ValidateLedger(events, request);
        if (technology != DemoTechnologies.A2A)
            foreach (var role in AgentNames.Specialists)
            {
                var emitted = 0;
                await ExpectFailure("unknown_agent", () => runtime.ExecuteAsync(request with
                {
                    Configuration = request.Configuration with { AgentModels = new() { [role] = "gpt5" } }
                }, _ => { emitted++; return Task.CompletedTask; }));
                Check(emitted == 0, "Inactive agent overrides fail preflight before model, skill or HTTP execution");
            }
    }

    private static async Task ValidateConcurrentAuthority(IAgentRuntime runtime, string technology)
    {
        var permittedRequest = Request(technology, "Prepara la bozza per ORD-1001 difettoso.",
            new() { ConfirmAction = true }) with { CustomerId = "CUST-DEMO-02" };
        var forbiddenRequest = Request(technology, "Mostra ORD-1001, usa customerId=CUST-DEMO-02.");
        var unconfirmedRequest = Request(technology, "Prepara la bozza per ORD-1042 difettoso.");
        var outcomes = await Task.WhenAll(
            Execute(runtime, permittedRequest), Execute(runtime, forbiddenRequest), Execute(runtime, unconfirmedRequest));
        Check(outcomes[0].Result.Decision == "draft" && outcomes[0].Result.Sources.Contains("order:ORD-1001"),
            $"{technology} concurrent trusted customer and confirmation reach their own service operation");
        Check(outcomes[1].Result.ProductIds.Count == 0 && !outcomes[1].Result.Sources.Contains("order:ORD-1001")
              && outcomes[1].Events.Any(item => item.Kind == "tool.called" && item.Message == "get_order"
                  && JsonSerializer.SerializeToElement(item.Data, AgentJson.Options).GetProperty("status").GetString() == "failed"),
            $"{technology} concurrent trusted customer never leaks to another run");
        Check(outcomes[2].Result.Decision != "draft"
              && !outcomes[2].Events.Any(item => item.Kind == "tool.called" && item.Message == "create_return_draft"),
            $"{technology} concurrent confirmation never authorizes an unconfirmed run");
        foreach (var (outcome, request) in outcomes.Zip(new[] { permittedRequest, forbiddenRequest, unconfirmedRequest }))
            ValidateLedger(outcome.Events, request);
    }

    private static async Task ValidateA2ACancellation(SpecialistServices services)
    {
        foreach (var (role, message) in new[]
        {
            (AgentNames.Catalog, "Cerco una camicia sotto 40 USD."),
            (AgentNames.Orders, "Mostra l'ordine ORD-1042."),
            (AgentNames.Returns, "ORD-1042 ha un difetto, qual è il rimborso?")
        })
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = Request(DemoTechnologies.A2A, message);
            var events = new List<RunEvent>();
            var cancelledDuringDrain = false;
            try
            {
                await services.Runtime.ExecuteAsync(request, item =>
                {
                    events.Add(item);
                    if (item.Kind == "model.completed" && item.Agent == role)
                    {
                        cancelledDuringDrain = true;
                        cancellation.Cancel();
                    }
                    return Task.CompletedTask;
                }, cancellation.Token);
                throw new InvalidOperationException("Expected cancellation during the actual remote ledger drain.");
            }
            catch (OperationCanceledException)
            {
                Check(cancelledDuringDrain, $"Cancellation is triggered by real {role} remote model evidence, not a timeout");
                ValidateRemoteLedger(services, events, request);
                Check(events.Any(item => item.Kind == "agent.completed" && item.Agent == role),
                    $"Cancellation preserves the complete already-captured {role} lifecycle");
                Check(!events.Any(item => item.Kind == "answer.delta"), "Cancelled A2A never returns a successful answer");
                Check(events.Select(item => item.Id).Distinct().Count() == events.Count,
                    "Cancellation drain does not duplicate imported evidence");
            }
        }
    }

    private static async Task ValidateServiceOutages(TrackingShopData data)
    {
        foreach (var (role, message) in new[]
        {
            (AgentNames.Catalog, "Cerco una camicia sotto 40 USD."),
            (AgentNames.Orders, "Mostra l'ordine ORD-1042."),
            (AgentNames.Returns, "ORD-1042 ha un difetto, qual è il rimborso?")
        })
        {
            await using var services = await SpecialistServices.StartAsync(data);
            await services.Hosts[role].StopAsync();
            foreach (var technology in new[] { DemoTechnologies.Inline, DemoTechnologies.Skills })
            {
                var request = Request(technology, message);
                var events = new List<RunEvent>();
                var requestStart = services.Requests.Count;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await services.Runtime.ExecuteAsync(request, item => { events.Add(item); return Task.CompletedTask; }, deadline.Token);
                    throw new InvalidOperationException($"Expected explicit {role} HTTP service outage.");
                }
                catch (ShopServiceException error)
                {
                    Check(error.Message.Contains(role, StringComparison.Ordinal), "Business service outage identifies the actual unavailable service");
                    Check(!events.Any(item => item.Kind == "answer.delta"), "Business transport failure never becomes a success-shaped fallback answer");
                    Check(Calls(events).All(call => call.Agent == AgentNames.Router), "Unavailable HTTP service is never replaced by a fake local specialist");
                    Check(events.Any(item => item.Kind == "protocol.response"
                        && JsonSerializer.SerializeToElement(item.Data, AgentJson.Options) is var payload
                        && payload.GetProperty("protocol").GetString() == "HTTP"
                        && payload.GetProperty("service").GetString() == role
                        && payload.GetProperty("status").GetString() == "failed"),
                        $"{technology} persists failed {role} HTTP evidence");
                    Check(!services.Requests.Skip(requestStart).Any(actual => actual.Role == role),
                        "Stopped Kestrel host receives no request; successful domain data cannot come from an in-process fallback");
                }
            }
        }
    }

    private static async Task ValidateMalformedServiceResponse(IShopData data)
    {
        var malformedResponses = 0;
        await using var services = await SpecialistServices.StartAsync(data, (role, host) =>
        {
            if (role != AgentNames.Orders) return;
            host.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (context.Request.Path == "/orders/ORD-1042")
                {
                    Interlocked.Increment(ref malformedResponses);
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("""{"id":"ORD-1042"}""");
                    return;
                }
                await next(context);
            });
        });
        foreach (var technology in new[] { DemoTechnologies.Inline, DemoTechnologies.Skills })
        {
            var before = malformedResponses;
            var events = new List<RunEvent>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await services.Runtime.ExecuteAsync(Request(technology, "Mostra l'ordine ORD-1042."),
                    item => { events.Add(item); return Task.CompletedTask; }, deadline.Token);
                throw new InvalidOperationException("Expected the incomplete HTTP 200 order DTO to fail the run.");
            }
            catch (ShopServiceException error)
            {
                Check(malformedResponses == before + 1 && error.InnerException is JsonException,
                    $"{technology} rejects a real HTTP 200 DTO missing required constructor fields instead of fabricating zero-valued order facts");
                Check(!events.Any(item => item.Kind == "answer.delta"),
                    "Malformed service success data never becomes a success-shaped answer or local fallback");
                Check(events.Any(item => item.Kind == "tool.called" && item.Message == "get_order"
                      && JsonSerializer.SerializeToElement(item.Data, AgentJson.Options).GetProperty("status").GetString() == "failed"),
                    "Malformed success DTO is recorded as a failed actual order tool");
                Check(events.Any(item => item.Kind == "protocol.response" && item.Agent == AgentNames.Router
                      && JsonSerializer.SerializeToElement(item.Data, AgentJson.Options) is var response
                      && response.GetProperty("protocol").GetString() == "HTTP"
                      && response.GetProperty("service").GetString() == AgentNames.Orders
                      && response.GetProperty("status").GetString() == "failed"),
                    "Malformed response preserves explicit router/Orders HTTP failure evidence");
            }
        }
    }
}
