using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.Router.Skills.RemoteSkills;

/// <summary>
/// Agent Skills loaded from the three remote skill sites (the Skills analogue of A2A agent cards).
/// Only the index is read up front; SKILL.md and references are downloaded when the model asks for them.
/// </summary>
public sealed class RemoteSkillsSource(HttpClient http, RunState state) : AgentSkillsSource
{
    // The only trusted hosts: resolved by Aspire service discovery.
    public static readonly string[] Sites = ["skill-catalog", "skill-orders", "skill-returns"];

    private IList<AgentSkill>? _skills;

    public override async Task<IList<AgentSkill>> GetSkillsAsync(AgentSkillsSourceContext context, CancellationToken cancellationToken)
    {
        if (_skills is not null) return _skills;
        var skills = new List<AgentSkill>();
        foreach (var site in Sites)
        {
            var index = await CallAsync(site, "/skills", async response =>
                await response.Content.ReadFromJsonAsync<SkillIndexEntry[]>(cancellationToken).ConfigureAwait(false) ?? [],
                cancellationToken).ConfigureAwait(false);
            skills.AddRange(index.Select(entry => new RemoteSkill(this, site, entry)));
        }
        return _skills = skills;
    }

    internal Task<string> ReadMarkdownAsync(string site, string path, CancellationToken cancellationToken) =>
        CallAsync(site, path, async response =>
        {
            if (response.Content.Headers.ContentType?.MediaType != "text/markdown")
                throw new DomainException("skill_unavailable", $"{site} non ha restituito Markdown per {path}.");
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    // HTTP call with timeline events; any failure stops the run (fail closed with skill_unavailable).
    private async Task<T> CallAsync<T>(string site, string path, Func<HttpResponseMessage, Task<T>> read, CancellationToken cancellationToken)
    {
        var url = new Uri($"http://{site}{path}");
        var operation = $"GET {path}";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = null;
        await state.EmitAsync("protocol.request", AgentNames.Router, $"HTTP {site}: {operation}.",
            new { protocol = "HTTP", service = site, operation, url = url.AbsoluteUri }).ConfigureAwait(false);
        try
        {
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new DomainException("skill_unavailable", $"{site} ha risposto {(int)response.StatusCode} per {path}.");
            return await read(response).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            failure = error is DomainException ? error : new DomainException("skill_unavailable", $"Skill site {site} non raggiungibile: {error.Message}");
            state.Fail(failure);
            throw failure;
        }
        finally
        {
            await state.EmitAsync("protocol.response", AgentNames.Router, $"HTTP {site}: {operation}.", new
            {
                protocol = "HTTP", service = site, operation, url = url.AbsoluteUri,
                durationMs = watch.Elapsed.TotalMilliseconds,
                status = failure is null ? "completed" : "failed",
                error = failure is null ? null : SafeTelemetry.Text(failure.Message)
            }).ConfigureAwait(false);
        }
    }

    private sealed record SkillIndexEntry(string Name, string Description, string[] Resources);

    private sealed class RemoteSkill(RemoteSkillsSource source, string site, SkillIndexEntry entry) : AgentSkill
    {
        public override AgentSkillFrontmatter Frontmatter { get; } = new(entry.Name, entry.Description, null);

        public override async ValueTask<string> GetContentAsync(CancellationToken cancellationToken) =>
            await source.ReadMarkdownAsync(site, $"/skills/{entry.Name}/SKILL.md", cancellationToken).ConfigureAwait(false);

        // Only resources listed in the index can be read: no arbitrary paths.
        public override ValueTask<AgentSkillResource> GetResourceAsync(string name, CancellationToken cancellationToken) =>
            entry.Resources.Contains(name)
                ? ValueTask.FromResult<AgentSkillResource>(new RemoteSkillResource(source, site, entry.Name, name))
                : throw new DomainException("skill_resource_unknown", $"La skill {entry.Name} non pubblica la risorsa {name}.");
    }

    private sealed class RemoteSkillResource(RemoteSkillsSource source, string site, string skill, string name)
        : AgentSkillResource(name, $"Risorsa {name} della skill {skill}.")
    {
        public override async Task<object> ReadAsync(IServiceProvider services, CancellationToken cancellationToken) =>
            await source.ReadMarkdownAsync(site, $"/skills/{skill}/{name}", cancellationToken).ConfigureAwait(false);
    }
}
