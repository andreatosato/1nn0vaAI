using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Observatory.Core;

namespace Observatory.AgentRuntime;

/// <summary>
/// Router-side A2A client: discovers a specialist through its agent card, sends one message and imports the
/// specialist's evidence (model calls, tools, costs) from the reply metadata. The model only sees the answer text.
/// </summary>
public sealed class SpecialistClient(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "a2a";
    public const string RunMetadataKey = "observatory.run";
    public const string EvidenceMetadataKey = "observatory.evidence";
    public const string ErrorMetadataKey = "observatory.error";
    public const string SdkVersion = "0.3.4-preview";

    /// <summary>Logical Aspire resource name of a specialist agent, resolved by service discovery.</summary>
    public static Uri AgentEndpoint(string role) => new($"http://agent-{role}/");

    public async Task<string> InvokeAsync(RunState state, string role, string query, CancellationToken cancellationToken)
    {
        if (!state.Request.Configuration.UnboundedExecution && state.RemainingCalls <= 0)
            throw new DomainException("model_call_limit", "Nessuna chiamata disponibile per lo specialista remoto.");
        var http = httpClients.CreateClient(HttpClientName);
        var baseUri = AgentEndpoint(role);
        var cardPath = $"/a2a/{role}/.well-known/agent-card.json";
        var cardUrl = new Uri(baseUri, cardPath).AbsoluteUri;
        await state.EmitAsync("protocol.request", role, "A2A agent-card discovery.", new { protocol = "A2A", sdk = SdkVersion, method = "GET", url = cardUrl }).ConfigureAwait(false);
        AgentCard card;
        try
        {
            card = await new A2ACardResolver(baseUri, http, cardPath).GetAgentCardAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await state.EmitAsync("protocol.response", role, "A2A discovery fallita; nessun fallback.", new
            {
                protocol = "A2A", url = cardUrl,
                status = error is OperationCanceledException ? "cancelled" : "failed",
                error = SafeTelemetry.Text(error.Message)
            }).ConfigureAwait(false);
            if (error is OperationCanceledException) throw;
            throw new DomainException("a2a_discovery_failed", $"Discovery A2A non riuscita: {SafeTelemetry.Text(error.Message)}");
        }
        if (!Uri.TryCreate(card.Url, UriKind.Absolute, out var endpoint) || !endpoint.AbsolutePath.Equals($"/a2a/{role}", StringComparison.Ordinal))
            throw new DomainException("a2a_card_mismatch", "La card A2A pubblicizza un endpoint diverso dallo specialista richiesto.");
        await state.EmitAsync("protocol.response", role, "A2A agent card verificata.", new { card.Name, card.Url, card.ProtocolVersion, sdk = SdkVersion }).ConfigureAwait(false);

        var invocationId = Guid.NewGuid().ToString("N");
        var remoteRequest = state.Request with
        {
            Configuration = state.Request.Configuration.UnboundedExecution ? state.Request.Configuration : state.Request.Configuration with
            {
                MaxModelCalls = state.RemainingCalls,
                ApprovedBudgetUsd = state.RemainingBudget
            }
        };
        var parameters = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = invocationId,
                ContextId = state.Request.ConversationId,
                Parts = [new TextPart { Text = query }]
            },
            Metadata = new() { [RunMetadataKey] = JsonSerializer.SerializeToElement(new RemoteInvocation(invocationId, remoteRequest), AgentJson.Options) }
        };
        await state.EmitAsync("protocol.request", role, "A2A message/send (official SDK JSON-RPC).", new
        {
            protocol = "A2A", sdk = SdkVersion, method = "message/send", url = endpoint.AbsoluteUri,
            invocationId, message = SafeTelemetry.Text(query)
        }).ConfigureAwait(false);

        AgentMessage message;
        try
        {
            var response = await new A2AClient(endpoint, http).SendMessageAsync(parameters, cancellationToken).ConfigureAwait(false);
            message = response as AgentMessage
                ?? throw new DomainException("a2a_unexpected_response", "L'host sincrono deve restituire un AgentMessage A2A.");
        }
        catch (Exception error) when (error is not (OperationCanceledException or DomainException))
        {
            await state.EmitAsync("protocol.response", role, "A2A richiesta fallita; nessun fallback.", new { invocationId, error = SafeTelemetry.Text(error.Message) }).ConfigureAwait(false);
            throw new DomainException("a2a_transport_failed", SafeTelemetry.Text(error.Message));
        }

        var evidence = await ImportEvidenceAsync(state, message).ConfigureAwait(false);
        await state.EmitAsync("protocol.response", role, "A2A risposta dominio ricevuta.", new
        {
            protocol = "A2A", invocationId, importedEvents = evidence,
            parts = message.Parts.Count
        }).ConfigureAwait(false);
        if (message.Metadata?.TryGetValue(ErrorMetadataKey, out var failure) == true)
        {
            var error = failure.Deserialize<SpecialistError>(AgentJson.Options)!;
            throw new DomainException(error.Code, error.Message);
        }
        var text = string.Concat(message.Parts.OfType<TextPart>().Select(part => part.Text));
        var result = JsonSerializer.Deserialize<AgentExecutionResult>(text, AgentJson.Options)
            ?? throw new DomainException("a2a_invalid_domain", "Risposta dominio A2A non valida.");
        state.ObserveDomain(result);
        return result.Answer;
    }

    /// <summary>Reads the specialist's own prompt preview; each agent owns its instructions.</summary>
    public async Task<AgentPromptPreview> PreviewAsync(string role, RunConfiguration configuration, CancellationToken cancellationToken)
    {
        using var response = await httpClients.CreateClient(HttpClientName)
            .PostAsJsonAsync(new Uri(AgentEndpoint(role), "prompts/preview"), configuration, AgentJson.Options, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentPromptPreview>(AgentJson.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("a2a_invalid_preview", $"Anteprima del prompt di {role} non valida.");
    }

    private static async Task<int> ImportEvidenceAsync(RunState state, AgentMessage message)
    {
        if (message.Metadata?.TryGetValue(EvidenceMetadataKey, out var evidence) != true)
            throw new DomainException("a2a_evidence_missing", "La risposta A2A non contiene le evidenze dello specialista.");
        var events = evidence.Deserialize<RunEvent[]>(AgentJson.Options) ?? [];
        foreach (var item in events)
            await state.PublishAsync(item, imported: true).ConfigureAwait(false);
        return events.Length;
    }
}

/// <summary>Domain failure reported by a specialist together with its evidence.</summary>
public sealed record SpecialistError(string Code, string Message);
