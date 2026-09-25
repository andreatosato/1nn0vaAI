using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Observatory.AgentHost;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Api;

internal static class DemoDataHttpChecks
{
    public static async Task<int> Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "demo-data-checks", Guid.NewGuid().ToString("N"));
        var hosts = new List<WebApplication>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var requests = new ConcurrentQueue<(string Role, string Method, string Path, string Customer, string Confirmation)>();
        string? failingRole = null;
        var failureStatus = 200;
        var failureBody = "";
        var waitForCancellation = false;
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        try
        {
            var data = new ShopData(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            var configuration = new Dictionary<string, string?> { ["Demo:AllowLive"] = "false", ["Agents:AllowRemote"] = "false" };
            foreach (var role in AgentNames.Specialists)
            {
                var builder = Builder();
                builder.Services.AddSingleton<IShopData>(data);
                builder.Services.AddSingleton(new AgentModelRegistry(builder.Configuration));
                var host = builder.Build();
                hosts.Add(host);
                host.Use((HttpContext context, RequestDelegate next) =>
                {
                    requests.Enqueue((role, context.Request.Method, context.Request.Path,
                        context.Request.Headers[ShopServiceClient.CustomerHeader].ToString(),
                        context.Request.Headers[ShopServiceClient.ConfirmationHeader].ToString()));
                    return next(context);
                });
                host.Use(async (HttpContext context, RequestDelegate next) =>
                {
                    if (role == failingRole)
                    {
                        if (waitForCancellation)
                        {
                            requestStarted.TrySetResult();
                            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
                            catch (OperationCanceledException) { requestCancelled.TrySetResult(); }
                            return;
                        }
                        context.Response.StatusCode = failureStatus;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(failureBody, context.RequestAborted);
                        return;
                    }
                    try { await next(context); }
                    catch (DomainException error)
                    {
                        await Results.Problem(statusCode: BusinessEndpoints.StatusFor(error.Code),
                            extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context);
                    }
                });
                host.MapBusinessEndpoints(role);
                await host.StartAsync(deadline.Token);
                configuration[$"Agents:Endpoints:{role}"] = Address(host);
            }

            var apiBuilder = Builder();
            configuration["Storage:Path"] = Path.Combine(directory, "must-not-exist.sqlite");
            apiBuilder.Configuration.AddInMemoryCollection(configuration);
            apiBuilder.Services.AddProblemDetails();
            apiBuilder.Services.AddSingleton<ObservatorySettings>();
            apiBuilder.Services.AddSingleton<EvidenceSanitizer>();
            apiBuilder.Services.AddSingleton<IShopCatalog>(_ => throw new InvalidOperationException("Inspector resolved catalog."));
            apiBuilder.Services.AddSingleton<EvidenceStore>(_ => throw new InvalidOperationException("Inspector created evidence state."));
            apiBuilder.Services.AddSingleton<RunCoordinator>(_ => throw new InvalidOperationException("Inspector created run state."));
            apiBuilder.Services.AddSingleton<ExperimentService>(_ => throw new InvalidOperationException("Inspector created experiments."));
            apiBuilder.Services.AddSingleton<IAgentRuntime>(_ => throw new InvalidOperationException("Inspector resolved agent runtime."));
            apiBuilder.Services.AddObservatoryAgents(apiBuilder.Configuration);
            var api = apiBuilder.Build();
            hosts.Add(api);
            api.UseObservatoryErrors();
            api.MapObservatory();
            await api.StartAsync(deadline.Token);
            Check(api.Services.GetService<IShopData>() is null, "API must have no local shop-data registration.");
            using var client = new HttpClient { BaseAddress = new(Address(api)), Timeout = TimeSpan.FromSeconds(10) };
            for (var iteration = 0; iteration < 2; iteration++)
            {
                using var response = await client.GetAsync("/api/demo-data", deadline.Token);
                Check(response.StatusCode == HttpStatusCode.OK, "Inspector must return 200 over real HTTP.");
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: deadline.Token);
                Check(json.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(
                    new[] { "asOf", "customerId", "notice", "orders", "policies" }.Order()), "Response shape is exact.");
                var snapshot = json.Deserialize<DemoDataResponse>(ApiJson.Options)!;
                Check(snapshot.AsOf == DemoClock.AsOf && snapshot.CustomerId == DemoClock.CustomerId, "Frozen clock and trusted customer are unchanged.");
                Check(snapshot.Notice.Contains("altri clienti") && snapshot.Notice.Contains("didattica")
                    && snapshot.Notice.Contains("chat") && snapshot.Notice.Contains("invariate"), "Notice explains teaching scope and unchanged chat authorization.");
                Check(snapshot.Orders.Count == 50 && snapshot.Orders.SequenceEqual(data.DemoOrders), "All 50 orders originate unchanged from Orders.");
                Check(snapshot.Policies.Count == 12 && snapshot.Policies.SequenceEqual(data.Policies.OrderByDescending(p => p.Priority)),
                    "All 12 policies originate from Returns in priority order, including archived policies.");
                var main = snapshot.Orders.Single(order => order.Id == "ORD-1042");
                Check(main is { ListPrice: 29.99m, AmountPaid: 19.99m, ProductId: 83, Outlet: true, Currency: "USD" }
                    && main.DeliveredAt == new DateOnly(2026, 9, 5), "Main order authoritative facts remain intact.");
                Check(snapshot.Orders.Any(order => order.CustomerId != DemoClock.CustomerId), "Inspector includes other synthetic customers.");
                var orderJson = json.GetProperty("orders")[0];
                Check(orderJson.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[]
                    { "id", "customerId", "productId", "productTitle", "listPrice", "amountPaid", "currency", "outlet", "deliveredAt", "status", "trackingCode" }.Order()),
                    "Order JSON fields are exact.");
                Check(orderJson.GetProperty("deliveredAt").GetString()!.Length == 10, "Delivery serializes as date only.");
                Check(json.GetProperty("policies")[0].EnumerateObject().Select(p => p.Name).Order().SequenceEqual(
                    new[] { "id", "title", "text", "priority", "version" }.Order()), "Policy JSON fields are exact.");
            }
            Check(requests.Count == 4 && requests.All(request => request.Method == "GET" && request.Customer == "" && request.Confirmation == ""
                && (request.Role, request.Path) is (AgentNames.Orders, "/demo-data/orders") or (AgentNames.Returns, "/policies")),
                "Only Orders/Returns read-only business HTTP is used: no Catalog, model, A2A, customer or confirmation headers.");
            Check(typeof(ShopServiceClient).GetInterfaces().SelectMany(contract => contract.GetMethods())
                    .All(method => !method.Name.Contains("Demo", StringComparison.Ordinal)),
                "Inspector is not exposed through the agent operations contract.");
            foreach (var role in new[] { AgentNames.Catalog, AgentNames.Returns })
            {
                using var other = new HttpClient { BaseAddress = new(configuration[$"Agents:Endpoints:{role}"]!) };
                using var response = await other.GetAsync("/demo-data/orders", deadline.Token);
                Check(response.StatusCode == HttpStatusCode.NotFound, "Only Orders hosts the UI-only order route.");
            }
            using (var ordersClient = new HttpClient { BaseAddress = new(configuration["Agents:Endpoints:orders"]!) })
            {
                ordersClient.DefaultRequestHeaders.Add(ShopServiceClient.CustomerHeader, DemoClock.CustomerId);
                using var denied = await ordersClient.GetAsync("/orders/ORD-1001", deadline.Token);
                Check(denied.StatusCode == HttpStatusCode.NotFound, "Inspector never widens customer-scoped order authorization.");
                using var unconfirmed = await ordersClient.PostAsJsonAsync("/return-drafts",
                    new ReturnOperationRequest("ORD-1042", "defect"), deadline.Token);
                Check(unconfirmed.StatusCode == HttpStatusCode.Forbidden, "Explicit confirmation remains required after inspection.");
            }

            foreach (var role in new[] { AgentNames.Orders, AgentNames.Returns })
            {
                failingRole = role;
                foreach (var body in new[] { "", "null", "[]", "{}", "[{}]", "[null]", "[", """[{"id":"incomplete"}]""" })
                {
                    failureBody = body;
                    await CheckBadGateway();
                }
                var node = JsonSerializer.SerializeToNode(role == AgentNames.Orders
                    ? (object)data.DemoOrders : data.Policies, ApiJson.Options)!.AsArray();
                node[0]!["id"] = "";
                failureBody = node.ToJsonString();
                await CheckBadGateway();
                node[0]!["id"] = node[1]!["id"]!.GetValue<string>();
                failureBody = node.ToJsonString();
                await CheckBadGateway();
                failureStatus = 503;
                failureBody = """{"detail":"upstream unavailable"}""";
                await CheckBadGateway();
                failureStatus = 200;
            }
            failingRole = AgentNames.Orders;
            waitForCancellation = true;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                var pending = api.Services.GetRequiredService<ShopServiceClient>().GetDemoDataAsync(cancellation.Token);
                await requestStarted.Task.WaitAsync(deadline.Token);
                cancellation.Cancel();
                try { await pending; throw new InvalidOperationException("Cancellation was swallowed."); }
                catch (OperationCanceledException) { checks++; }
                await requestCancelled.Task.WaitAsync(deadline.Token);
                checks++;
            }
            Check(!Directory.EnumerateFileSystemEntries(directory).Any(), "Inspector creates no drafts, conversations, runs, databases or state files.");
            Console.WriteLine($"PASS demo-data HTTP checks: {checks}; real role routes, strict responses, authorization, cancellation, no state/model side effects.");
            return 0;

            async Task CheckBadGateway()
            {
                using var response = await client.GetAsync("/api/demo-data", deadline.Token);
                Check(response.StatusCode == HttpStatusCode.BadGateway, "Invalid/unavailable upstream must fail with 502, never a partial/local snapshot.");
                Check(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Upstream failure uses Problem Details.");
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: deadline.Token);
                Check(problem.GetProperty("code").GetString() == "shop_service_unavailable"
                    && problem.GetProperty("traceId").GetString()!.Length > 0, "Problem Details has stable code and trace ID.");
            }
        }
        finally
        {
            foreach (var host in hosts.AsEnumerable().Reverse())
            {
                await host.StopAsync(CancellationToken.None);
                await host.DisposeAsync();
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        return builder;
    }

    private static string Address(WebApplication host) =>
        host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
}
