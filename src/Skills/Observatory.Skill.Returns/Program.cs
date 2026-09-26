using System.Text.RegularExpressions;

// Skill site of the remote Returns specialist: it publishes the 'returns' skill (its instructions) over HTTP.
// GET /skills is the index, the Skills analogue of an A2A agent card. The Skills router downloads SKILL.md
// and the references only when the model loads them. No model runs here.
const string Skill = "returns";

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();
app.MapDefaultEndpoints();

var folder = Path.Combine(app.Environment.ContentRootPath, "skills", Skill);
var markdown = File.ReadAllText(Path.Combine(folder, "SKILL.md"));
var description = Regex.Match(markdown, @"^description:\s*(.+?)\s*$", RegexOptions.Multiline).Groups[1].Value;
var references = Directory.Exists(Path.Combine(folder, "references"))
    ? Directory.GetFiles(Path.Combine(folder, "references"), "*.md").Select(file => $"references/{Path.GetFileName(file)}").Order().ToArray()
    : [];

app.MapGet("/skills", () => new[] { new { name = Skill, description, resources = references } });
app.MapGet($"/skills/{Skill}/SKILL.md", () => Markdown(markdown));
foreach (var reference in references)
{
    var text = File.ReadAllText(Path.Combine(folder, reference));
    app.MapGet($"/skills/{Skill}/{reference}", () => Markdown(text));
}
app.Run();

static IResult Markdown(string text) => Results.Text(text, "text/markdown; charset=utf-8");

namespace Observatory.Skill.Returns
{
    /// <summary>Entry point marker for tests (WebApplicationFactory).</summary>
    public sealed class SkillSite;
}
