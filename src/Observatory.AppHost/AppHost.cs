using Microsoft.Extensions.Configuration;

// AI Observatory: three architectures for the same shop assistant, each agent in its own process.
//
//   Shop business APIs (no model)      shop-catalog, shop-orders, shop-returns
//   A2A specialist agents (model)      agent-catalog, agent-orders, agent-returns  → call the shop APIs
//   Skill sites (no model)             skill-catalog, skill-orders, skill-returns → publish the specialists as skills
//   Routers (model, one per demo)      router-inline                            → calls the shop APIs
//                                      router-skills                            → loads remote skills, calls the shop APIs
//                                      router-a2a                               → delegates to the agents
var builder = DistributedApplication.CreateBuilder(args);
var workspace = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var state = Path.Combine(workspace, ".appdata");
foreach (var folder in new[] { "catalog", "domain", "inline", "skills", "a2a" })
    Directory.CreateDirectory(Path.Combine(state, folder));
var azureKey = string.IsNullOrWhiteSpace(builder.Configuration["Parameters:azure-openai-key"])
    ? null : builder.AddParameter("azure-openai-key", secret: true);

// Business APIs: the systems of record. They share the synthetic shop state on disk.
var shopCatalog = AddShopApi(builder.AddProject<Projects.Observatory_Shop_Catalog>("shop-catalog"));
var shopOrders = AddShopApi(builder.AddProject<Projects.Observatory_Shop_Orders>("shop-orders"));
var shopReturns = AddShopApi(builder.AddProject<Projects.Observatory_Shop_Returns>("shop-returns"));

// Specialist agents: one process, one model and one prompt per domain.
var agentCatalog = AddAgent(builder.AddProject<Projects.Observatory_Agent_Catalog>("agent-catalog")).WithShop(shopCatalog);
var agentOrders = AddAgent(builder.AddProject<Projects.Observatory_Agent_Orders>("agent-orders")).WithShop(shopOrders, shopReturns);
var agentReturns = AddAgent(builder.AddProject<Projects.Observatory_Agent_Returns>("agent-returns")).WithShop(shopReturns);

// Skill sites: the same three specialists, published as Agent Skills instead of A2A agents.
var skillCatalog = builder.AddProject<Projects.Observatory_Skill_Catalog>("skill-catalog").WithHttpHealthCheck("/health");
var skillOrders = builder.AddProject<Projects.Observatory_Skill_Orders>("skill-orders").WithHttpHealthCheck("/health");
var skillReturns = builder.AddProject<Projects.Observatory_Skill_Returns>("skill-returns").WithHttpHealthCheck("/health");

// Routers: the UI talks to these. Every router also reads catalog and demo data for the UI.
var inline = AddRouter(builder.AddProject<Projects.Observatory_Router_Inline>("router-inline"), "inline");
var skills = AddRouter(builder.AddProject<Projects.Observatory_Router_Skills>("router-skills"), "skills")
    .WithReference(skillCatalog).WaitFor(skillCatalog)
    .WithReference(skillOrders).WaitFor(skillOrders)
    .WithReference(skillReturns).WaitFor(skillReturns);
var a2a = AddRouter(builder.AddProject<Projects.Observatory_Router_A2A>("router-a2a"), "a2a")
    .WithReference(agentCatalog).WaitFor(agentCatalog)
    .WithReference(agentOrders).WaitFor(agentOrders)
    .WithReference(agentReturns).WaitFor(agentReturns);

builder.AddViteApp("web", Path.Combine(workspace, "src", "Observatory.Web"))
    .WithEndpoint("http", endpoint => endpoint.Port = null)
    .WithEnvironment("INLINE_API_URL", inline.GetEndpoint("http"))
    .WithEnvironment("SKILLS_API_URL", skills.GetEndpoint("http"))
    .WithEnvironment("A2A_API_URL", a2a.GetEndpoint("http"))
    .WaitFor(inline).WaitFor(skills).WaitFor(a2a);

builder.Build().Run();

IResourceBuilder<ProjectResource> AddShopApi(IResourceBuilder<ProjectResource> api) => api
    .WithEnvironment("Data__Directory", Path.Combine(state, "catalog"))
    .WithEnvironment("Shop__StatePath", Path.Combine(state, "domain"))
    .WithHttpHealthCheck("/health");

IResourceBuilder<ProjectResource> AddAgent(IResourceBuilder<ProjectResource> agent) => WithModels(agent)
    .WithHttpHealthCheck("/health");

IResourceBuilder<ProjectResource> AddRouter(IResourceBuilder<ProjectResource> router, string technology) => WithModels(router)
    .WithEnvironment("Storage__Path", Path.Combine(state, technology, "observatory.sqlite"))
    .WithShop(shopCatalog, shopOrders, shopReturns)
    .WithHttpHealthCheck("/health");

// Model deployments, rate cards and the LIVE/unbounded opt-ins come from AppHost configuration (user secrets).
IResourceBuilder<ProjectResource> WithModels(IResourceBuilder<ProjectResource> resource)
{
    resource.WithEnvironment("Demo__AllowLive", builder.Configuration.GetValue<bool>("Demo:AllowLive").ToString())
        .WithEnvironment("Demo__AllowUnboundedExecution", builder.Configuration.GetValue<bool>("Demo:AllowUnboundedExecution").ToString());
    if (builder.Configuration["Demo:MaxApprovedBudgetUsd"] is { Length: > 0 } budget)
        resource.WithEnvironment("Demo__MaxApprovedBudgetUsd", budget);
    if (builder.Configuration["AzureOpenAI:Endpoint"] is { Length: > 0 } endpoint)
        resource.WithEnvironment("AzureOpenAI__Endpoint", endpoint);
    if (azureKey is not null)
        resource.WithEnvironment("AzureOpenAI__ApiKey", azureKey);
    foreach (var (key, value) in builder.Configuration.GetSection("Models").AsEnumerable())
        if (!string.IsNullOrWhiteSpace(value))
            resource.WithEnvironment(key.Replace(":", "__"), value);
    return resource;
}

static class ShopReferences
{
    public static IResourceBuilder<ProjectResource> WithShop(this IResourceBuilder<ProjectResource> resource,
        params IResourceBuilder<ProjectResource>[] apis)
    {
        foreach (var api in apis) resource.WithReference(api).WaitFor(api);
        return resource;
    }
}
