using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Observatory.Skills.Tests;

/// <summary>HTTP contract of the skill sites, as consumed by the Skills router.</summary>
public abstract class SkillSiteContract<TSite>(WebApplicationFactory<TSite> site, string skill) where TSite : class
{
    protected HttpClient Client { get; } = site.CreateClient();

    [Fact]
    public async Task Index_lists_only_its_own_skill()
    {
        using var index = JsonDocument.Parse(await Client.GetStringAsync("/skills"));
        var entry = Assert.Single(index.RootElement.EnumerateArray());
        Assert.Equal(skill, entry.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task Serves_SKILL_md_as_markdown()
    {
        var response = await Client.GetAsync($"/skills/{skill}/SKILL.md");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"name: {skill}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("orders")]
    [InlineData("returns")]
    public async Task Does_not_serve_other_skills(string other)
    {
        if (other == skill) return;
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/skills/{other}/SKILL.md")).StatusCode);
    }

    [Fact]
    public async Task Exposes_the_aspire_health_endpoint() =>
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health")).StatusCode);
}

public sealed class CatalogSkillSiteTests(WebApplicationFactory<Observatory.Skill.Catalog.SkillSite> site)
    : SkillSiteContract<Observatory.Skill.Catalog.SkillSite>(site, "catalog"), IClassFixture<WebApplicationFactory<Observatory.Skill.Catalog.SkillSite>>;

public sealed class OrdersSkillSiteTests(WebApplicationFactory<Observatory.Skill.Orders.SkillSite> site)
    : SkillSiteContract<Observatory.Skill.Orders.SkillSite>(site, "orders"), IClassFixture<WebApplicationFactory<Observatory.Skill.Orders.SkillSite>>;

public sealed class ReturnsSkillSiteTests(WebApplicationFactory<Observatory.Skill.Returns.SkillSite> site)
    : SkillSiteContract<Observatory.Skill.Returns.SkillSite>(site, "returns"), IClassFixture<WebApplicationFactory<Observatory.Skill.Returns.SkillSite>>
{
    [Fact]
    public async Task Publishes_the_decision_checklist_reference()
    {
        using var index = JsonDocument.Parse(await Client.GetStringAsync("/skills"));
        Assert.Contains("references/decision-checklist.md",
            index.RootElement[0].GetProperty("resources").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("Non selezionare mai un ordine di un altro cliente",
            await Client.GetStringAsync("/skills/returns/references/decision-checklist.md"));
    }
}
