using Microsoft.Extensions.Hosting;
using Observatory.AgentRuntime;

namespace Observatory.Agents.Tests;

public sealed class ModelCredentialTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Managed_identity_is_skipped_only_in_local_development(string environmentName, bool excluded)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
            DisableDefaults = true
        });

        var options = AzureOpenAIChatClientProvider.CredentialOptions(builder.Environment);

        Assert.Equal(excluded, options.ExcludeManagedIdentityCredential);
        Assert.True(options.ExcludeInteractiveBrowserCredential);
        Assert.False(options.ExcludeAzureCliCredential);
        Assert.False(options.ExcludeVisualStudioCredential);
        Assert.False(options.ExcludeEnvironmentCredential);
        Assert.False(options.ExcludeWorkloadIdentityCredential);
    }
}
