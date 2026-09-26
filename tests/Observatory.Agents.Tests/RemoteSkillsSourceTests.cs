using System.Net;
using System.Text;
using Observatory.AgentRuntime;
using Observatory.Core;
using Observatory.Router.Skills.RemoteSkills;

namespace Observatory.Agents.Tests;

public sealed class RemoteSkillsSourceTests
{
    [Fact]
    public async Task Reads_only_the_indexes_until_the_model_loads_a_skill()
    {
        var sites = new FakeSites();
        var source = new RemoteSkillsSource(new HttpClient(sites), NewState());

        var skills = await source.GetSkillsAsync(null!, CancellationToken.None);

        Assert.Equal(["catalog", "orders", "returns"], skills.Select(skill => skill.Frontmatter.Name));
        Assert.Equal(["http://skill-catalog/skills", "http://skill-orders/skills", "http://skill-returns/skills"], sites.Requests);

        var returns = skills.Single(skill => skill.Frontmatter.Name == "returns");
        Assert.Contains("# Specialista returns", await returns.GetContentAsync(CancellationToken.None));
        var checklist = await returns.GetResourceAsync("references/decision-checklist.md", CancellationToken.None);
        Assert.Equal("checklist", await checklist.ReadAsync(null!, CancellationToken.None));
        Assert.Equal(["http://skill-returns/skills/returns/SKILL.md", "http://skill-returns/skills/returns/references/decision-checklist.md"],
            sites.Requests.Skip(3));
    }

    [Fact]
    public async Task Rejects_resources_that_are_not_in_the_index()
    {
        var skills = await new RemoteSkillsSource(new HttpClient(new FakeSites()), NewState()).GetSkillsAsync(null!, CancellationToken.None);

        var error = await Assert.ThrowsAsync<DomainException>(async () =>
            await skills[0].GetResourceAsync("../../secrets.md", CancellationToken.None));
        Assert.Equal("skill_resource_unknown", error.Code);
    }

    [Fact]
    public async Task Fails_closed_when_a_skill_site_is_down()
    {
        var state = NewState();
        var source = new RemoteSkillsSource(new HttpClient(new FakeSites { Down = "skill-orders" }), state);

        var error = await Assert.ThrowsAsync<DomainException>(() => source.GetSkillsAsync(null!, CancellationToken.None));

        Assert.Equal("skill_unavailable", error.Code);
        Assert.Equal("skill_unavailable", Assert.Throws<DomainException>(state.ThrowIfFaulted).Code);
    }

    private static RunState NewState() => new(new AgentRunRequest
    {
        RunId = "skills", ConversationId = "skills", Technology = DemoTechnologies.Skills, Message = "skills",
        Configuration = new RunConfiguration { ModelProfileId = "gpt5", PromptProfile = "good", HistoryStrategy = "full" }
    }, _ => Task.CompletedTask);

    private sealed class FakeSites : HttpMessageHandler
    {
        public string? Down { get; init; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            Requests.Add(url.AbsoluteUri);
            var skill = url.Host["skill-".Length..];
            if (url.Host == Down) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(url.AbsolutePath switch
            {
                "/skills" => Text($$"""[{"name":"{{skill}}","description":"Istruzioni {{skill}}","resources":{{(skill == "returns" ? "[\"references/decision-checklist.md\"]" : "[]")}}}]""", "application/json"),
                var path when path == $"/skills/{skill}/SKILL.md" => Text($"---\nname: {skill}\n---\n# Specialista {skill}", "text/markdown"),
                "/skills/returns/references/decision-checklist.md" => Text("checklist", "text/markdown"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }

        private static HttpResponseMessage Text(string body, string mediaType) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    }
}
