using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Observatory.Agent.Catalog;
using Observatory.Agent.Orders;
using Observatory.Agent.Returns;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Router.A2A;
using Observatory.Router.Inline;
using Observatory.Router.Skills;

namespace Observatory.Agents.Tests;

/// <summary>
/// Each agent must still send exactly the instructions and tools that were sent to the model in the recorded
/// LIVE measurements (see Golden/README.md). Moving agents into their own projects must not change behaviour.
/// </summary>
public sealed class GoldenRequestTests
{
    // Tools injected at runtime by the native Agent Skills provider, not by the router.
    private static readonly string[] SkillProviderTools = ["load_skill", "read_skill_resource", "run_skill_script"];

    public static TheoryData<string> Agents => new()
    {
        "inline-router", "skills-router", "a2a-router", "a2a-catalog", "a2a-orders", "a2a-returns"
    };

    [Theory]
    [MemberData(nameof(Agents))]
    public void Instructions_match_the_recorded_request(string name)
    {
        var (technology, _) = Split(name);
        var request = Request(technology);

        var instructions = Create(name).Instructions(request);

        var recorded = Golden(name, "instructions.txt");
        if (name == "skills-router")
        {
            // The native Agent Skills provider appends its own instructions after the router's at runtime.
            Assert.StartsWith(instructions + "\n", recorded);
            Assert.StartsWith("You have access to skills", recorded[(instructions.Length + 1)..]);
        }
        else Assert.Equal(recorded, instructions);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void Tools_match_the_recorded_request(string name)
    {
        var (technology, _) = Split(name);
        var state = new RunState(Request(technology), _ => Task.CompletedTask);

        var actual = Describe(Create(name).Tools(state));

        var expected = JsonNode.Parse(Golden(name, "tools.json"))!.AsArray()
            .Where(tool => !SkillProviderTools.Contains(tool!["name"]!.GetValue<string>()))
            .ToArray();
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
            Assert.True(JsonNode.DeepEquals(expected[index], actual[index]),
                $"{name} tool {index}: expected {expected[index]!.ToJsonString()} but was {actual[index]!.ToJsonString()}");
    }

    private static (string Technology, string Agent) Split(string name) => (name[..name.IndexOf('-')], name[(name.IndexOf('-') + 1)..]);

    private static AgentRunRequest Request(string technology) => new()
    {
        RunId = "golden", ConversationId = "golden", Technology = technology, Message = "golden",
        Configuration = new RunConfiguration { ModelProfileId = "gpt5", PromptProfile = "good", HistoryStrategy = "full" }
    };

    private static IAgent Create(string name) => name switch
    {
        "inline-router" => new InlineRouter(null!, null!),
        "skills-router" => new SkillsRouter(null!, null!, null!),
        "a2a-router" => new A2ARouter(null!, null!),
        "a2a-catalog" => new CatalogAgent(null!, null!),
        "a2a-orders" => new OrdersAgent(null!, null!),
        "a2a-returns" => new ReturnsAgent(null!, null!),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static string Golden(string name, string kind) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", $"{name}.{kind}"));

    // Same projection the evidence capture uses for request.tools.
    private static List<JsonNode?> Describe(IEnumerable<AITool> tools) => tools.OfType<AIFunction>()
        .Select(tool => JsonSerializer.SerializeToNode(new
        {
            tool.Name, tool.Description, parameters = SafeTelemetry.Snapshot(tool.JsonSchema)
        }, AgentJson.Options))
        .ToList();
}
