using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Observatory.ServiceDefaults;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Observatory.AgentRuntime;

/// <summary>Creates the raw provider client for one model deployment. Tests replace it with an offline fake.</summary>
public interface IChatClientProvider
{
    IChatClient Create(ModelRegistration registration);
}

public sealed class AzureOpenAIChatClientProvider(AgentModelRegistry registry, IHostEnvironment environment) : IChatClientProvider
{
    internal static DefaultAzureCredentialOptions CredentialOptions(IHostEnvironment environment) => new()
    {
        ExcludeInteractiveBrowserCredential = true,
        // IMDS belongs to the Azure host, not the developer's machine.
        ExcludeManagedIdentityCredential = environment.IsDevelopment()
    };

    public IChatClient Create(ModelRegistration registration)
    {
        var endpoint = new Uri(registry.Configuration["AzureOpenAI:Endpoint"]!);
        var options = new AzureOpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(0) };
        options.AddPolicy(new ProviderUsageCapturePolicy(), PipelinePosition.PerCall);
        var key = registry.Configuration["AzureOpenAI:ApiKey"];
        var client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(CredentialOptions(environment)), options)
            : new AzureOpenAIClient(endpoint, new ApiKeyCredential(key), options);
        return client.GetChatClient(registration.Model.Deployment!).AsIChatClient();
    }
}

/// <summary>
/// Model client pipeline for one agent: provider → evidence capture (tokens, cost, logical request) → native OpenTelemetry.
/// </summary>
public sealed class ModelClientFactory(AgentModelRegistry registry, IChatClientProvider provider)
{
    internal IChatClient Create(RunState state, string agent)
    {
        var registration = registry.ForAgent(state.Request, agent);
        return new ModelCaptureChatClient(provider.Create(registration), state, agent, registration).AsBuilder()
            .UseObservatoryTelemetry()
            .Build();
    }
}
