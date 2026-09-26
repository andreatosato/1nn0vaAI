using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Observatory.AgentRuntime;

public static class AgentRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddAgentRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.TryAddSingleton(new AgentModelRegistry(configuration));
        services.TryAddSingleton<IChatClientProvider, AzureOpenAIChatClientProvider>();
        services.TryAddSingleton<ModelClientFactory>();
        services.TryAddSingleton<AgentRunner>();
        services.TryAddSingleton<ShopServiceClient>();
        services.TryAddSingleton<SpecialistClient>();

        // Aspire service discovery resolves the logical names used by these clients (for example http://shop-catalog).
        // No retry handler: a retried model or A2A call can repeat billable work.
        services.AddHttpClient(ShopServiceClient.HttpClientName);
        services.AddHttpClient(SpecialistClient.HttpClientName, http => http.Timeout = Timeout.InfiniteTimeSpan);
        return services;
    }
}
