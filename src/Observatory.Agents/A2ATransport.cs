using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Observatory.Core;

namespace Observatory.Agents;

public sealed class A2ATransport(AgentModelRegistry registry, AgentTransportAccess access) : IDisposable
{
    public const string MetadataKey = "observatory.run";
    public const string SdkVersion = "0.3.4-preview";
    private readonly HttpClient _http = access.CreateHttpClient();
    private readonly HttpClient _unboundedHttp = access.CreateHttpClient(Timeout.InfiniteTimeSpan);

    public async Task<string> InvokeAsync(RunState state, string role, string query, CancellationToken cancellationToken)
    {
        var baseUri = registry.ServiceEndpoint(role);
        access.ValidateEndpoint(baseUri);
        var http = state.Request.Configuration.UnboundedExecution ? _unboundedHttp : _http;
        if (!state.Request.Configuration.UnboundedExecution && state.RemainingCalls <= 0)
            throw new DomainException("model_call_limit", "Nessuna chiamata disponibile per lo specialista remoto.");
        var path = $"a2a/{role}";
        var endpoint = new Uri(baseUri, path);
        var cardPath = $"/{path}/.well-known/agent-card.json";
        var cardUrl = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + cardPath).AbsoluteUri;
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
                protocol = "A2A",
                url = cardUrl,
                status = error is OperationCanceledException ? "cancelled" : "failed",
                error = access.Redact(error.Message)
            }).ConfigureAwait(false);
            if (error is OperationCanceledException) throw;
            throw new DomainException("a2a_discovery_failed", $"Discovery A2A non riuscita: {access.Redact(error.Message)}");
        }
        if (!Uri.TryCreate(card.Url, UriKind.Absolute, out var advertised) || advertised != endpoint)
            throw new DomainException("a2a_card_mismatch", "La card A2A pubblicizza un endpoint diverso da quello configurato; nessun redirect implicito.");
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
        var envelope = new RemoteInvocation(invocationId, remoteRequest);
        var parameters = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = invocationId,
                ContextId = state.Request.ConversationId,
                Parts = [new TextPart { Text = query }]
            },
            Metadata = new() { [MetadataKey] = JsonSerializer.SerializeToElement(envelope, AgentJson.Options) }
        };
        await state.EmitAsync("protocol.request", role, "A2A message/send (official SDK JSON-RPC).", new
        {
            protocol = "A2A",
            sdk = SdkVersion,
            method = "message/send",
            url = endpoint.AbsoluteUri,
            invocationId,
            message = SafeTelemetry.Text(query)
        }).ConfigureAwait(false);
        A2AResponse? response = null;
        Exception? failure = null;
        try
        {
            response = await new A2AClient(endpoint, http).SendMessageAsync(parameters, cancellationToken).ConfigureAwait(false);
            await state.EmitAsync("protocol.response", role, "A2A risposta dominio ricevuta.", new
            {
                protocol = "A2A",
                invocationId,
                payload = SafeTelemetry.Snapshot(response)
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var safeMessage = access.Redact(error.Message);
            failure = safeMessage == error.Message ? error : new DomainException("a2a_transport_failed", safeMessage);
            await state.EmitAsync("protocol.response", role, "A2A richiesta fallita; nessun fallback.", new { invocationId, error = safeMessage }).ConfigureAwait(false);
        }
        finally
        {
            // Telemetry uses a separate protected channel, never A2A message parts or tool results.
            await ImportTelemetryAsync(baseUri, state, role, invocationId).ConfigureAwait(false);
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (response is not AgentMessage message)
            throw new DomainException("a2a_unexpected_response", "L'host sincrono deve restituire un AgentMessage A2A.");
        var text = string.Concat(message.Parts.OfType<TextPart>().Select(part => part.Text));
        var result = JsonSerializer.Deserialize<AgentExecutionResult>(text, AgentJson.Options)
            ?? throw new DomainException("a2a_invalid_domain", "Risposta dominio A2A non valida.");
        state.ObserveDomain(result);
        return result.Answer;
    }

    private async Task ImportTelemetryAsync(Uri baseUri, RunState state, string role, string invocationId)
    {
        var uri = new Uri(baseUri, $"telemetry/{Uri.EscapeDataString(state.Request.RunId)}?invocationId={invocationId}");
        await state.EmitAsync("protocol.request", role, "Raccolta telemetria separata dal contesto del modello.", new
        {
            protocol = access.AllowRemote ? "internal-authenticated-telemetry" : "internal-loopback-telemetry",
            method = "GET",
            url = uri.AbsoluteUri
        }).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                using var httpResponse = await _http.GetAsync(uri, timeout.Token).ConfigureAwait(false);
                if (httpResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    httpResponse.EnsureSuccessStatusCode();
                    var batch = await httpResponse.Content.ReadFromJsonAsync<RemoteTelemetryBatch>(AgentJson.Options, timeout.Token).ConfigureAwait(false)
                        ?? throw new DomainException("a2a_telemetry_missing", "Telemetria remota mancante.");
                    if (batch.RunId != state.Request.RunId || batch.InvocationId != invocationId)
                        throw new DomainException("a2a_telemetry_mismatch", "Correlazione della telemetria remota non valida.");
                    foreach (var item in batch.Events)
                        await state.PublishAsync(item, imported: true).ConfigureAwait(false);
                    if (batch.Completed)
                    {
                        await state.EmitAsync("protocol.response", role, "Tutti gli eventi remoti importati una sola volta.", new
                        {
                            protocol = access.AllowRemote ? "internal-authenticated-telemetry" : "internal-loopback-telemetry",
                            invocationId,
                            eventCount = batch.Events.Count,
                            complete = true
                        }).ConfigureAwait(false);
                        return;
                    }
                }
                await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is not DomainException)
        {
            throw new DomainException("a2a_telemetry_incomplete", $"La raccolta della telemetria remota non è completa: {access.Redact(error.Message)}");
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _unboundedHttp.Dispose();
    }
}
