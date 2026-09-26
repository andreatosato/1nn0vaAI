using Microsoft.Agents.AI;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.Skills.Api;

public sealed class SkillsAgent(AgentSession session, ShopServiceClient shop, AgentModelRegistry registry) : IAgentRuntime
{
    public async Task<AgentExecutionResult> ExecuteAsync(AgentRunRequest request, Func<RunEvent, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (request.Technology != DemoTechnologies.Skills)
            throw new DomainException("invalid_architecture", "Questo processo esegue soltanto Skills.");
        var result = await session.RunAsync(AgentNames.Router, request, AgentPrompts.History(request),
            state =>
            {
                var tools = new ShopFunctions(shop, state);
                return [.. tools.Catalog(), .. tools.Orders(), .. tools.Returns()];
            }, emit, cancellationToken, LoadSkills);
        await emit(new RunEvent
        {
            RunId = request.RunId, Kind = "answer.delta", Agent = AgentNames.Router,
            Message = result.Answer, Data = new { text = result.Answer, buffered = true }
        });
        return result;
    }

    private AIContextProvider LoadSkills()
    {
        var builder = new AgentSkillsProviderBuilder();
        foreach (var service in new[] { "catalog", "orders", "returns" })
        {
            var directory = Path.Combine(registry.SkillsDirectory, service);
            if (!Directory.Exists(directory))
                throw new DomainException("skills_missing", $"Directory della skill assente: {directory}.");
            builder.UseFileSkill(directory, new AgentFileSkillsSourceOptions
            {
                AllowedResourceExtensions = [".md"],
                ScriptFilter = _ => false
            }, (_, _, _, _, _) => throw new NotSupportedException("Skill scripts are disabled; only trusted Markdown is loaded."));
        }
        return builder.UseOptions(options =>
        {
            options.DisableLoadSkillApproval = true;
            options.DisableReadSkillResourceApproval = true;
        }).Build();
    }
}
