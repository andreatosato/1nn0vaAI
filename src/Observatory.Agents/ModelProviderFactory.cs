using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Observatory.Core;
using Observatory.ServiceDefaults;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Observatory.Agents;

public sealed class ModelProviderFactory(AgentModelRegistry registry)
{
    internal IChatClient Create(RunState state, string agent)
    {
        var registration = registry.ForAgent(state.Request, agent);
        var endpoint = new Uri(registry.Configuration["AzureOpenAI:Endpoint"]!);
        var options = new AzureOpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(0) };
        options.AddPolicy(new ProviderUsageCapturePolicy(), PipelinePosition.PerCall);
        var key = registry.Configuration["AzureOpenAI:ApiKey"];
        var client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true
            }), options)
            : new AzureOpenAIClient(endpoint, new ApiKeyCredential(key), options);
        var provider = client.GetChatClient(registration.Model.Deployment!).AsIChatClient();
        return new ModelCaptureChatClient(provider, state, agent, registration).AsBuilder()
            .UseObservatoryTelemetry()
            .Build();
    }
}
