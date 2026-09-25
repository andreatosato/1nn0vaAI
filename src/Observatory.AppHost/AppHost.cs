using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);
var workspace = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var stateRoot = Path.Combine(workspace, ".appdata");
Directory.CreateDirectory(stateRoot);
Directory.CreateDirectory(Path.Combine(stateRoot, "catalog"));
Directory.CreateDirectory(Path.Combine(stateRoot, "domain"));
var specialistRoles = new[] { "catalog", "orders", "returns" };

IResourceBuilder<ParameterResource>? azureKey = null;
if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:azure-openai-key"]))
{
    azureKey = builder.AddParameter("azure-openai-key", secret: true);
}

var useContainers = builder.Configuration.GetValue<bool>("UseContainers");
IResourceBuilder<ParameterResource>? agentTransportSecret = null;
if (useContainers)
{
    var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    agentTransportSecret = builder.AddParameter("a2a-transport-key", () => secret, secret: true);
}

if (useContainers)
{
    var agentImage = AddImageBuild("publish-agent-image", new Projects.Observatory_AgentHost().ProjectPath);
    // The two publishes share project outputs, so do not build them concurrently.
    var apiImage = AddImageBuild("publish-api-image", new Projects.Observatory_Api().ProjectPath)
        .WaitForCompletion(agentImage);

    var services = specialistRoles.ToDictionary(role => role, role => AddContainerService(role, agentImage));

    var inline = AddContainerDemo("inline", services, apiImage);
    var skills = AddContainerDemo("skills", services, apiImage);
    var a2a = AddContainerDemo("a2a", services, apiImage);
    builder.AddDockerfile("web", workspace, Path.Combine("src", "Observatory.Web", "Dockerfile"))
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithEnvironment("INLINE_API_URL", inline.GetEndpoint("http"))
        .WithEnvironment("SKILLS_API_URL", skills.GetEndpoint("http"))
        .WithEnvironment("A2A_API_URL", a2a.GetEndpoint("http"))
        .WaitFor(inline).WaitFor(skills).WaitFor(a2a);
}
else
{
    var services = specialistRoles.ToDictionary(role => role, AddProcessService);

    var inline = AddProcessDemo("inline", services);
    var skills = AddProcessDemo("skills", services);
    var a2a = AddProcessDemo("a2a", services);
    builder.AddViteApp("web", Path.Combine(workspace, "src", "Observatory.Web"))
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithEnvironment("INLINE_API_URL", inline.GetEndpoint("http"))
        .WithEnvironment("SKILLS_API_URL", skills.GetEndpoint("http"))
        .WithEnvironment("A2A_API_URL", a2a.GetEndpoint("http"))
        .WaitFor(inline).WaitFor(skills).WaitFor(a2a);
}

builder.Build().Run();

IResourceBuilder<T> Configure<T>(IResourceBuilder<T> resource) where T : IResourceWithEnvironment
{
    var allowLive = builder.Configuration.GetValue<bool?>("Demo:AllowLive")
        ?? builder.Configuration.GetValue<bool>("AllowLive");
    resource.WithEnvironment("Demo__AllowLive", allowLive.ToString())
        .WithEnvironment("Demo__AllowUnboundedExecution", builder.Configuration.GetValue<bool>("Demo:AllowUnboundedExecution").ToString())
        .WithEnvironment("Demo__DefaultMode", builder.Configuration["Demo:DefaultMode"] ?? "live")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
    if (builder.Configuration["Demo:MaxApprovedBudgetUsd"] is { Length: > 0 } budget)
    {
        resource.WithEnvironment("Demo__MaxApprovedBudgetUsd", budget);
    }

    if (agentTransportSecret is not null)
    {
        resource.WithEnvironment("Agents__AllowRemote", "true")
            .WithEnvironment("Agents__SharedSecret", agentTransportSecret);
    }

    var endpoint = builder.Configuration["AzureOpenAI:Endpoint"];
    if (!string.IsNullOrWhiteSpace(endpoint))
    {
        resource.WithEnvironment("AzureOpenAI__Endpoint", endpoint);
    }
    if (azureKey is not null)
    {
        resource.WithEnvironment("AzureOpenAI__ApiKey", azureKey);
    }

    foreach (var model in new[] { "gpt5", "gpt6-astra", "gpt6-sol", "gpt6-luna" })
    {
        foreach (var (key, value) in builder.Configuration.GetSection($"Models:{model}").AsEnumerable())
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                resource.WithEnvironment(key.Replace(":", "__"), value);
            }
        }
    }
    return resource;
}

IResourceBuilder<T> ConfigureService<T>(IResourceBuilder<T> resource, string role)
    where T : IResourceWithEnvironment
{
    return Configure(resource)
        .WithEnvironment("Agents__Role", role)
        .WithEnvironment("Data__Directory", useContainers ? "/state/catalog" : Path.Combine(stateRoot, "catalog"))
        .WithEnvironment("Shop__StatePath", useContainers ? "/state/domain" : Path.Combine(stateRoot, "domain"));
}

IResourceBuilder<ProjectResource> AddProcessService(string role)
{
    return ConfigureService(builder.AddProject<Projects.Observatory_AgentHost>($"{role}-service", role), role)
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithHttpHealthCheck("/health");
}

IResourceBuilder<ProjectResource> AddProcessDemo(
    string technology,
    IReadOnlyDictionary<string, IResourceBuilder<ProjectResource>> services)
{
    var directory = Path.Combine(stateRoot, technology);
    Directory.CreateDirectory(directory);
    var demo = Configure(builder.AddProject<Projects.Observatory_Api>($"demo-{technology}", "http"))
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithEnvironment("Demo__Technology", technology)
        .WithEnvironment("Storage__Path", Path.Combine(directory, "observatory.sqlite"))
        .WithHttpHealthCheck("/health");
    foreach (var (role, service) in services)
    {
        demo.WithEnvironment($"Agents__Endpoints__{role}", service.GetEndpoint("http"))
            .WithReference(service)
            .WaitFor(service);
    }
    return demo;
}

IResourceBuilder<ExecutableResource> AddImageBuild(string name, string projectPath)
{
    return builder.AddExecutable(name, "dotnet", workspace,
        "publish", projectPath, "--configuration", "Release", "--os", "linux",
        "--no-self-contained", "/t:PublishContainer", "-p:ContainerRegistry=");
}

IResourceBuilder<ContainerResource> AddContainerService(
    string role,
    IResourceBuilder<ExecutableResource> agentImage)
{
    return ConfigureService(builder.AddContainer($"{role}-service", "ai-observatory-agent-host", "dev"), role)
        .WithImagePullPolicy(ImagePullPolicy.Never)
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithBindMount(Path.Combine(stateRoot, "catalog"), "/state/catalog")
        .WithBindMount(Path.Combine(stateRoot, "domain"), "/state/domain")
        .WithHttpHealthCheck("/health")
        .WithOtlpExporter()
        .WaitForCompletion(agentImage);
}

IResourceBuilder<ContainerResource> AddContainerDemo(
    string technology,
    IReadOnlyDictionary<string, IResourceBuilder<ContainerResource>> services,
    IResourceBuilder<ExecutableResource> apiImage)
{
    var directory = Path.Combine(stateRoot, technology);
    Directory.CreateDirectory(directory);
    var demo = Configure(builder.AddContainer($"demo-{technology}", "ai-observatory-api", "dev"))
        .WithImagePullPolicy(ImagePullPolicy.Never)
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithBindMount(directory, $"/state/{technology}")
        .WithEnvironment("Demo__Technology", technology)
        .WithEnvironment("Storage__Path", $"/state/{technology}/observatory.sqlite")
        .WithHttpHealthCheck("/health")
        .WithOtlpExporter()
        .WaitForCompletion(apiImage);
    foreach (var (role, service) in services)
    {
        demo.WithEnvironment($"Agents__Endpoints__{role}", service.GetEndpoint("http"))
            .WaitFor(service);
    }
    return demo;
}
