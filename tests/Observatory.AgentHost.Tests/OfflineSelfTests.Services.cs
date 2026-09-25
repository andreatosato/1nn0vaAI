using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

internal static partial class OfflineSelfTests
{
    private sealed class SpecialistServices : IAsyncDisposable
    {
        private ServiceProvider? _provider;
        public Dictionary<string, WebApplication> Hosts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Addresses { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<ServiceRequest> Requests { get; } = new();
        public IConfigurationRoot Configuration { get; private set; } = null!;
        public IAgentRuntime Runtime => _provider!.GetRequiredService<IAgentRuntime>();

        public static Task<SpecialistServices> StartAsync(IShopData? data, params string[] arguments) =>
            StartAsync(data, null, arguments);

        public static async Task<SpecialistServices> StartAsync(IShopData? data,
            Action<string, WebApplication>? configureHost, params string[] arguments)
        {
            var services = new SpecialistServices();
            try
            {
                foreach (var role in AgentNames.Specialists)
                {
                    var host = AgentHostApplication.Build(
                        ["--Agents:AllowRemote=false", .. arguments, $"--Agents:Role={role}"], data, selfTest: true);
                    host.Use((HttpContext context, RequestDelegate next) =>
                    {
                        services.Requests.Enqueue(new(role, context.Request.Method, context.Request.Path.Value ?? "",
                            context.Request.Headers[ShopServiceClient.CustomerHeader].ToString(),
                            context.Request.Headers[ShopServiceClient.ConfirmationHeader].ToString()));
                        return next(context);
                    });
                    configureHost?.Invoke(role, host);
                    services.Hosts.Add(role, host);
                    await host.StartAsync();
                    services.Addresses.Add(role, host.Services.GetRequiredService<IServer>()
                        .Features.Get<IServerAddressesFeature>()!.Addresses.Single());
                }
                Check(services.Addresses.Values.Distinct().Count() == 3, "Three specialist services bind three separate Kestrel origins");
                var settings = new Dictionary<string, string?>
                {
                    ["Demo:AllowLive"] = "false",
                    ["AllowLive"] = "false",
                    ["Agents:AllowRemote"] = "false"
                };
                foreach (var (role, address) in services.Addresses)
                {
                    settings[$"Agents:Endpoints:{role}"] = address;
                    foreach (var host in services.Hosts.Values)
                        host.Configuration[$"Agents:Endpoints:{role}"] = address;
                }
                services.Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).AddCommandLine(arguments).Build();
                services._provider = new ServiceCollection().AddObservatoryAgents(services.Configuration).BuildServiceProvider();
                Check(services._provider.GetService<IShopData>() is null && services._provider.GetService<IShopCatalog>() is null,
                    "Router runtime has no local shop-data or catalog registration to fall back to");
                foreach (var role in AgentNames.Specialists)
                {
                    using var client = services.CreateClient(role);
                    Check((await client.GetStringAsync("/health")).Contains("healthy", StringComparison.OrdinalIgnoreCase),
                        $"{role} Kestrel host is running and responsive");
                }
                return services;
            }
            catch
            {
                await services.DisposeAsync();
                throw;
            }
        }

        public HttpClient CreateClient(string role, bool authenticate = true)
        {
            var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
            {
                BaseAddress = new Uri(Addresses[role]),
                Timeout = TimeSpan.FromSeconds(30)
            };
            if (authenticate) new AgentTransportAccess(Configuration).AuthenticateClient(client);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            if (_provider is not null) await _provider.DisposeAsync();
            foreach (var host in Hosts.Values.Reverse())
            {
                await host.StopAsync();
                await host.DisposeAsync();
            }
        }
    }

    private sealed record ServiceRequest(string Role, string Method, string Path, string Customer, string Confirmation);

    private static async Task ValidateServiceRoutes(SpecialistServices services, IShopData data)
    {
        var routes = new (string Role, HttpMethod Method, string Path)[]
        {
            (AgentNames.Catalog, HttpMethod.Get, "/catalog"),
            (AgentNames.Catalog, HttpMethod.Get, "/catalog/query?category=vestiti&color=rosso"),
            (AgentNames.Catalog, HttpMethod.Get, "/catalog/facets"),
            (AgentNames.Catalog, HttpMethod.Get, "/products?query=camicia&maxPrice=40&take=2"),
            (AgentNames.Catalog, HttpMethod.Get, "/products/83"),
            (AgentNames.Orders, HttpMethod.Get, "/orders/ORD-1042"),
            (AgentNames.Orders, HttpMethod.Get, "/demo-data/orders"),
            (AgentNames.Orders, HttpMethod.Post, "/return-drafts"),
            (AgentNames.Returns, HttpMethod.Get, "/policies"),
            (AgentNames.Returns, HttpMethod.Post, "/return-assessments")
        };
        foreach (var (role, host) in services.Hosts)
        {
            using var client = services.CreateClient(role);
            foreach (var healthPath in new[] { "/health", "/alive" })
            {
                Check((await client.GetStringAsync(healthPath)).Contains("healthy", StringComparison.OrdinalIgnoreCase),
                    $"{role} exposes standard {healthPath}");
                Check(((IEndpointRouteBuilder)host).DataSources.SelectMany(source => source.Endpoints)
                        .OfType<RouteEndpoint>().Count(endpoint => endpoint.RoutePattern.RawText == healthPath) == 1,
                    $"{role} maps shared {healthPath} exactly once");
            }
            var metadata = await client.GetFromJsonAsync<JsonElement>("/", AgentJson.Options);
            Check(metadata.GetProperty("role").GetString() == role, $"{role} root metadata identifies only its own role");
            foreach (var other in AgentNames.Specialists.Where(item => item != role))
                Check(!metadata.GetRawText().Contains($"/a2a/{other}", StringComparison.Ordinal)
                      && !metadata.GetRawText().Contains($"/skills/shop-{other}", StringComparison.Ordinal),
                    $"{role} root metadata does not advertise {other} routes");

            foreach (var specialist in AgentNames.Specialists)
            {
                using var card = await client.GetAsync($"/a2a/{specialist}/.well-known/agent-card.json");
                using var skill = await client.GetAsync($"/skills/shop-{specialist}/SKILL.md");
                if (specialist == role)
                {
                    Check(card.IsSuccessStatusCode && (await card.Content.ReadAsStringAsync()).Contains($"shop-{role}"),
                        $"{role} exposes its official A2A card");
                    var advertised = await new A2ACardResolver(new Uri(services.Addresses[role]), client,
                        $"/a2a/{role}/.well-known/agent-card.json").GetAgentCardAsync();
                    Check(new Uri(advertised.Url) == new Uri(services.Addresses[role] + $"/a2a/{role}"),
                        $"{role} card advertises the real role-specific origin");
                    Check(skill.IsSuccessStatusCode && (await skill.Content.ReadAsStringAsync()).Contains($"name: shop-{role}"),
                        $"{role} exposes the actual service SKILL.md");
                }
                else
                {
                    Check(card.StatusCode == HttpStatusCode.NotFound && skill.StatusCode == HttpStatusCode.NotFound,
                        $"{role} refuses {specialist} discovery and skill routes");
                    using var message = await client.PostAsJsonAsync($"/a2a/{specialist}", new { }, AgentJson.Options);
                    Check(message.StatusCode == HttpStatusCode.NotFound, $"{role} cannot execute the {specialist} A2A endpoint");
                }
            }
            using (var routerSkill = await client.GetAsync("/skills/shop-router/SKILL.md"))
                Check(routerSkill.StatusCode == HttpStatusCode.NotFound, "There is no business-service shop-router skill");
            using (var resource = await client.GetAsync("/skills/shop-returns/references/decision-checklist.md"))
            {
                Check(resource.StatusCode == (role == AgentNames.Returns ? HttpStatusCode.OK : HttpStatusCode.NotFound),
                    "Only returns serves its referenced checklist");
                if (role == AgentNames.Returns)
                    Check((await resource.Content.ReadAsStringAsync()).Contains("Non selezionare mai un ordine di un altro cliente"),
                        "Returns resource endpoint serves real Markdown");
            }
            foreach (var route in routes.Where(route => route.Role != role))
            {
                using var response = await SendBusiness(client, route.Method, route.Path, DemoClock.CustomerId, true,
                    route.Method == HttpMethod.Post ? new { orderId = "ORD-1042", reason = "defect" } : null);
                Check(response.StatusCode == HttpStatusCode.NotFound, $"{role} exposes no foreign business route {route.Method} {route.Path}");
            }
        }
        using var catalog = services.CreateClient(AgentNames.Catalog);
        var snapshot = await catalog.GetFromJsonAsync<CatalogSnapshot>("/catalog", AgentJson.Options);
        Check(snapshot is not null && snapshot.ContentHash == data.Catalog.ContentHash && snapshot.Products.Count == 38
              && snapshot.Products.Any(product => !string.IsNullOrWhiteSpace(product.Thumbnail)),
            "Catalog metadata endpoint returns the complete UI snapshot with provenance and UI-only images");
        using var products = await catalog.GetAsync("/products?query=camicia&maxPrice=40&take=2");
        var factJson = await products.Content.ReadAsStringAsync();
        var facts = JsonSerializer.Deserialize<ProductFact[]>(factJson, AgentJson.Options);
        Check(products.IsSuccessStatusCode && facts is { Length: 2 }
              && facts.SequenceEqual(data.SearchProducts("camicia", 40, 2)),
            "Catalog search applies query, budget, take and the authoritative ordering over HTTP");
        Check(!factJson.Contains("\"thumbnail\"", StringComparison.OrdinalIgnoreCase)
              && !factJson.Contains("\"images\"", StringComparison.OrdinalIgnoreCase),
            "Business product facts never contain image decorations");
        var product = await catalog.GetFromJsonAsync<ProductFact>("/products/83", AgentJson.Options);
        Check(product == data.GetProduct(83) && product.Price == 29.99m, "Product detail endpoint preserves exact list price");
        var queryResult = await catalog.GetFromJsonAsync<CatalogQueryResponse>(
            "/catalog/query?category=scarpe&color=rosso&inStockOnly=true&take=1", AgentJson.Options);
        Check(queryResult is { TotalProducts: 4, InStockProducts: 4, StockUnits: 93, HasMore: true }
              && queryResult.Products.Count == 1 && queryResult.Products[0].Id == 189
              && queryResult.Filters is { Category: "shoes", Color: "red", InStockOnly: true, Take: 1 },
            "Catalog HTTP query preserves complete totals independently of its bounded, sorted examples and normalized filters");
        var empty = await catalog.GetFromJsonAsync<CatalogQueryResponse>(
            "/catalog/query?category=vestiti&color=rosso&maxPrice=100", AgentJson.Options);
        Check(empty is { TotalProducts: 0, StockUnits: 0, HasMore: false } && empty.Products.Count == 0,
            "Valid zero-match HTTP queries are explicit, grounded empty results");
        var facets = await catalog.GetFromJsonAsync<CatalogFacetsResponse>("/catalog/facets", AgentJson.Options);
        Check(facets is { TotalProducts: 38 } && facets.Categories.Sum(facet => facet.ProductCount) == 38
              && facets.Colors.Single(facet => facet.Value == "red") == new CatalogFacet("red", 5, 155),
            "Category and textual-color facets are served by the authoritative Catalog service");
        var aggregateJson = JsonSerializer.Serialize(new { queryResult, facets }, AgentJson.Options);
        Check(!aggregateJson.Contains("\"images\"", StringComparison.OrdinalIgnoreCase)
              && !aggregateJson.Contains("\"thumbnail\"", StringComparison.OrdinalIgnoreCase)
              && !aggregateJson.Contains("ExpectedFacts", StringComparison.OrdinalIgnoreCase),
            "Aggregate and facet HTTP contracts contain no image decorations or golden scenario facts");
    }

    private static async Task ValidateBusinessContracts(SpecialistServices services, TrackingShopData data)
    {
        using var catalog = services.CreateClient(AgentNames.Catalog);
        using var orders = services.CreateClient(AgentNames.Orders);
        using var returns = services.CreateClient(AgentNames.Returns);
        var draftCallsBeforeInspection = data.DraftCalls;
        var demoOrders = await orders.GetFromJsonAsync<ShopOrder[]>("/demo-data/orders", AgentJson.Options);
        Check(demoOrders is { Length: 50 } && demoOrders.SequenceEqual(data.DemoOrders)
              && demoOrders.Any(order => order.CustomerId != DemoClock.CustomerId)
              && data.DraftCalls == draftCallsBeforeInspection,
            "Orders teaching endpoint exposes all 50 authoritative synthetic orders, without a customer header or writes");
        using (var response = await catalog.GetAsync("/products/999999"))
            await CheckProblem(response, HttpStatusCode.NotFound, "product_not_found");
        foreach (var query in new[] { "take=0", "take=31", "maxPrice=-1" })
        {
            using var response = await catalog.GetAsync("/products?" + query);
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_search");
        }
        foreach (var query in new[] { "take=0", "take=31", "maxPrice=-1", "color=red%20or%20black",
                     "query=" + new string('x', 513), "category=" + new string('x', 81), "color=" + new string('x', 41) })
        {
            using var response = await catalog.GetAsync("/catalog/query?" + query);
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_search");
        }
        foreach (var query in new[] { "take=oops", "maxPrice=oops", "inStockOnly=oops" })
        {
            using var response = await catalog.GetAsync("/catalog/query?" + query);
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_request");
        }
        var policies = await returns.GetFromJsonAsync<ShopPolicy[]>("/policies", AgentJson.Options);
        Check(policies is not null && policies.SequenceEqual(data.Policies.OrderByDescending(policy => policy.Priority)),
            "Returns serves the authoritative policies in priority order, including archived-policy provenance");
        using (var response = await SendBusiness(orders, HttpMethod.Get, "/orders/ORD-1042", DemoClock.CustomerId))
        {
            var order = await response.Content.ReadFromJsonAsync<ShopOrder>(AgentJson.Options);
            Check(response.IsSuccessStatusCode && order == data.GetOrder("ORD-1042")
                  && order is { AmountPaid: 19.99m, ListPrice: 29.99m },
                "Order HTTP endpoint preserves paid versus public list price and customer scope");
        }
        foreach (var path in new[] { "/orders/ORD-9999", "/orders/ORD-1001?customerId=CUST-DEMO-02" })
        {
            using var response = await SendBusiness(orders, HttpMethod.Get, path, DemoClock.CustomerId);
            await CheckProblem(response, HttpStatusCode.NotFound, "order_not_found");
            Check(!(await response.Content.ReadAsStringAsync()).Contains("CUST-DEMO-02"), "Order rejections disclose no other customer's identity");
        }
        foreach (var customer in new string?[] { null, "", "bad customer" })
        {
            using var order = await SendBusiness(orders, HttpMethod.Get, "/orders/ORD-1042", customer);
            await CheckProblem(order, HttpStatusCode.Unauthorized, "customer_context_required");
            using var assessment = await SendBusiness(returns, HttpMethod.Post, "/return-assessments", customer,
                body: new { orderId = "ORD-1042", reason = "defect" });
            await CheckProblem(assessment, HttpStatusCode.Unauthorized, "customer_context_required");
            using var draft = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", customer, true,
                new { orderId = "ORD-1042", reason = "defect" });
            await CheckProblem(draft, HttpStatusCode.Unauthorized, "customer_context_required");
        }
        using (var duplicate = new HttpRequestMessage(HttpMethod.Get, "/orders/ORD-1042"))
        {
            duplicate.Headers.TryAddWithoutValidation(ShopServiceClient.CustomerHeader, [DemoClock.CustomerId, "CUST-DEMO-02"]);
            using var response = await orders.SendAsync(duplicate);
            await CheckProblem(response, HttpStatusCode.Unauthorized, "customer_context_required");
        }
        foreach (var reason in new[] { "defect", "change-of-mind", "unknown" })
        {
            using var response = await SendBusiness(returns, HttpMethod.Post, "/return-assessments", DemoClock.CustomerId,
                body: new { orderId = "ORD-1042", reason });
            var assessment = await response.Content.ReadFromJsonAsync<ReturnAssessment>(AgentJson.Options);
            Check(response.IsSuccessStatusCode && assessment == data.AssessReturn("ORD-1042", reason),
                $"HTTP return assessment preserves {reason} decision, current policy, days and paid refund amount");
        }
        using (var response = await SendBusiness(returns, HttpMethod.Post, "/return-assessments", DemoClock.CustomerId,
                   body: new { orderId = "ORD-1001", reason = "defect" }))
            await CheckProblem(response, HttpStatusCode.NotFound, "order_not_found");
        using (var response = await SendBusiness(returns, HttpMethod.Post, "/return-assessments", DemoClock.CustomerId,
                   body: new { orderId = "ORD-1001", reason = "defect", customerId = "CUST-DEMO-02" }))
            await CheckProblem(response, HttpStatusCode.NotFound, "order_not_found");
        using (var response = await SendBusiness(returns, HttpMethod.Post, "/return-assessments", DemoClock.CustomerId,
                   body: new { orderId = "ORD-1042", reason = "invented-reason" }))
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_return_reason");
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", DemoClock.CustomerId, false,
                   new { orderId = "ORD-1042", reason = "defect" }))
            await CheckProblem(response, HttpStatusCode.Forbidden, "confirmation_required");
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", DemoClock.CustomerId, false,
                   new { orderId = "ORD-1042", reason = "defect", confirmed = true, confirmAction = true }))
            await CheckProblem(response, HttpStatusCode.Forbidden, "confirmation_required");
        using (var duplicate = new HttpRequestMessage(HttpMethod.Post, "/return-drafts"))
        {
            duplicate.Headers.Add(ShopServiceClient.CustomerHeader, DemoClock.CustomerId);
            duplicate.Headers.TryAddWithoutValidation(ShopServiceClient.ConfirmationHeader, ["true", "false"]);
            duplicate.Content = JsonContent.Create(new { orderId = "ORD-1042", reason = "defect" }, options: AgentJson.Options);
            using var response = await orders.SendAsync(duplicate);
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_request");
        }
        foreach (var (client, path) in new[] { (orders, "/return-drafts"), (returns, "/return-assessments") })
        {
            using var response = await SendBusiness(client, HttpMethod.Post, path, DemoClock.CustomerId, true,
                new { orderId = "", reason = "defect" });
            await CheckProblem(response, HttpStatusCode.BadRequest, "invalid_request");
        }
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", DemoClock.CustomerId, true,
                   new { orderId = "ORD-1042", reason = "change-of-mind" }))
            await CheckProblem(response, HttpStatusCode.UnprocessableEntity, "return_not_eligible");

        var drafts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", DemoClock.CustomerId, true,
                new { orderId = "ORD-1042", reason = "defect" });
            Check(response.StatusCode == HttpStatusCode.OK && response.Headers.Location is null,
                "Idempotent draft POST returns 200, never a fictitious created-resource location");
            return await response.Content.ReadFromJsonAsync<ReturnDraft>(AgentJson.Options);
        }));
        Check(drafts.All(draft => draft == drafts[0]) && drafts[0] is { Amount: 19.99m, Reason: "defect", Status: "draft-synthetic" },
            "Concurrent HTTP retries preserve the same exact synthetic draft");
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", DemoClock.CustomerId, false,
                   new { orderId = "ORD-1042", reason = "defect" }))
            await CheckProblem(response, HttpStatusCode.Forbidden, "confirmation_required");
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", "CUST-DEMO-02", true,
                   new { orderId = "ORD-1001", reason = "defect" }))
            Check(response.StatusCode == HttpStatusCode.OK, "Another trusted customer can create only their own draft");
        using (var response = await SendBusiness(orders, HttpMethod.Post, "/return-drafts", "CUST-DEMO-02", true,
                   new { orderId = "ORD-1001", reason = "change-of-mind" }))
            await CheckProblem(response, HttpStatusCode.Conflict, "draft_conflict");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            var owner = index % 2 == 0;
            using var response = await SendBusiness(orders, HttpMethod.Get, "/orders/ORD-1001",
                owner ? "CUST-DEMO-02" : DemoClock.CustomerId);
            if (owner)
            {
                var order = await response.Content.ReadFromJsonAsync<ShopOrder>(AgentJson.Options);
                Check(response.IsSuccessStatusCode && order?.CustomerId == "CUST-DEMO-02",
                    "Concurrent HTTP calls retain their own trusted customer header");
            }
            else
                await CheckProblem(response, HttpStatusCode.NotFound, "order_not_found");
        }));
    }

    private static async Task<HttpResponseMessage> SendBusiness(HttpClient client, HttpMethod method, string path,
        string? customer = null, bool confirmed = false, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (customer is not null) request.Headers.TryAddWithoutValidation(ShopServiceClient.CustomerHeader, customer);
        if (confirmed) request.Headers.Add(ShopServiceClient.ConfirmationHeader, "true");
        if (body is not null) request.Content = JsonContent.Create(body, options: AgentJson.Options);
        return await client.SendAsync(request);
    }

    private static async Task CheckProblem(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == status, $"Business rejection must be HTTP {(int)status}, received {(int)response.StatusCode}: {text}");
        Check(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Business rejections use Problem Details");
        var problem = JsonSerializer.Deserialize<JsonElement>(text, AgentJson.Options);
        Check(problem.GetProperty("status").GetInt32() == (int)status
              && problem.TryGetProperty("code", out var actualCode) && !string.IsNullOrWhiteSpace(actualCode.GetString())
              && (code is null || actualCode.GetString() == code),
            $"Business Problem Details preserve status and domain code {code ?? "(required)"}");
    }
}
