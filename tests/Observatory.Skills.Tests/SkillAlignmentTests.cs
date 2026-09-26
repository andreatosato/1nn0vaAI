namespace Observatory.Skills.Tests;

/// <summary>
/// The skill published by each site is the same Procedure the A2A specialist runs with:
/// the two architectures compare transport, not different instructions.
/// </summary>
public sealed class SkillAlignmentTests
{
    public static TheoryData<string, string> Skills => new()
    {
        { "Observatory.Skill.Catalog", Observatory.Agent.Catalog.CatalogAgent.Procedure },
        { "Observatory.Skill.Orders", Observatory.Agent.Orders.OrdersAgent.Procedure },
        { "Observatory.Skill.Returns", Observatory.Agent.Returns.ReturnsAgent.Procedure }
    };

    [Theory]
    [MemberData(nameof(Skills))]
    public void Skill_body_contains_the_agent_procedure_verbatim(string project, string procedure)
    {
        var skill = project.Split('.').Last().ToLowerInvariant();
        var path = Path.Combine(SourceRoot(), "src", "Skills", project, "skills", skill, "SKILL.md");
        Assert.Contains(Normalize(procedure), Normalize(File.ReadAllText(path)));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AiObservatory.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("AiObservatory.slnx non trovato.");
    }
}
