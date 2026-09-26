using System.Text.Json;
using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Observatory.AgentRuntime;
using Observatory.Core;

namespace Observatory.SpecialistHost;

/// <summary>
/// Hosting shared by the three specialist agents: Aspire service defaults, the official A2A server, the agent card
/// and a prompt preview endpoint. The agent itself (instructions and tools) lives in its own project.
/// </summary>
public static class SpecialistHostApplication
{
    public static WebApplicationBuilder CreateBuilder<TAgent>(string[] args) where TAgent : class, ISpecialistAgent
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddProblemDetails();
        builder.Services.AddAgentRuntime(builder.Configuration);
        builder.Services.AddSingleton<ISpecialistAgent, TAgent>();
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        var agent = app.Services.GetRequiredService<ISpecialistAgent>();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var (status, code) = error is DomainException domain ? (StatusCodes.Status400BadRequest, domain.Code) : (500, "service_error");
                await Results.Problem(statusCode: status, title: code, detail: error is DomainException ? error.Message : "Errore dello specialista.",
                    extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
            }
        });
        app.UseStatusCodePages();
        app.MapDefaultEndpoints();
        app.MapGet("/", () => TypedResults.Ok(new
        {
            service = app.Environment.ApplicationName,
            agent.Role,
            a2a = new { endpoint = $"/a2a/{agent.Role}", card = $"/a2a/{agent.Role}/.well-known/agent-card.json" },
            promptPreview = "/prompts/preview"
        }));
        app.MapPost("/prompts/preview", (RunConfiguration configuration) =>
                TypedResults.Ok(PromptLaboratory.For(agent.Role,
                    agent.Instructions(PromptLaboratory.PreviewRequest(DemoTechnologies.A2A, configuration)))))
            .WithSummary("Exact instructions of this agent for a configuration; no model, no tools, no state.");

        var taskManager = CreateTaskManager(app.Services, agent);
        app.MapA2A(taskManager, $"/a2a/{agent.Role}");
        app.MapGroup($"/a2a/{agent.Role}").MapWellKnownAgentCard(taskManager, $"/a2a/{agent.Role}");
        return app;
    }

    private static TaskManager CreateTaskManager(IServiceProvider services, ISpecialistAgent agent)
    {
        var manager = new TaskManager();
        manager.OnAgentCardQuery = (serverUrl, _) => Task.FromResult(new AgentCard
        {
            Name = agent.Role,
            Description = agent.CardDescription,
            Url = new Uri(new Uri(serverUrl), $"/a2a/{agent.Role}").AbsoluteUri,
            Version = "1.0.0",
            ProtocolVersion = "0.3.0",
            Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["application/json"],
            Skills =
            [
                new AgentSkill
                {
                    Id = $"shop-{agent.Role}",
                    Name = $"Shop {agent.Role}",
                    Description = agent.SkillDescription,
                    Tags = ["shop", agent.Role, "synthetic-demo"]
                }
            ]
        });
        manager.OnMessageReceived = async (message, token) =>
        {
            var envelope = ReadInvocation(message);
            var query = string.Join("\n", message.Message.Parts.OfType<TextPart>().Select(part => part.Text));
            if (string.IsNullOrWhiteSpace(query) || message.Message.Parts.Any(part => part is not TextPart))
                throw new A2AException("Only domain text messages are supported.", A2AErrorCode.ContentTypeNotSupported);

            // Evidence travels back in the reply metadata; the router's model only receives the answer text.
            var evidence = new List<RunEvent>();
            var metadata = new Dictionary<string, JsonElement>();
            string text;
            try
            {
                var result = await agent.ExecuteAsync(envelope.Request, query, item =>
                {
                    lock (evidence) evidence.Add(item);
                    return Task.CompletedTask;
                }, token);
                text = JsonSerializer.Serialize(result, AgentJson.Options);
            }
            catch (DomainException error)
            {
                text = error.Message;
                metadata[SpecialistClient.ErrorMetadataKey] = JsonSerializer.SerializeToElement(
                    new SpecialistError(error.Code, error.Message), AgentJson.Options);
            }
            lock (evidence)
                metadata[SpecialistClient.EvidenceMetadataKey] = JsonSerializer.SerializeToElement(evidence, AgentJson.Options);
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = message.Message.ContextId,
                Parts = [new TextPart { Text = text }],
                Metadata = metadata
            };
        };
        return manager;
    }

    private static RemoteInvocation ReadInvocation(MessageSendParams message)
    {
        if (message.Metadata?.TryGetValue(SpecialistClient.RunMetadataKey, out var json) != true)
            throw new A2AException($"Missing standard A2A metadata extension '{SpecialistClient.RunMetadataKey}'.", A2AErrorCode.InvalidParams);
        RemoteInvocation envelope;
        try
        {
            envelope = json.Deserialize<RemoteInvocation>(AgentJson.Options) ?? throw new JsonException("Empty run metadata.");
        }
        catch (JsonException error)
        {
            throw new A2AException("Invalid per-run metadata.", error, A2AErrorCode.InvalidParams);
        }
        if (!Guid.TryParseExact(envelope.InvocationId, "N", out _) || envelope.Request is null
            || message.Message.Role != MessageRole.User || message.Message.MessageId != envelope.InvocationId)
            throw new A2AException("Invalid invocation correlation.", A2AErrorCode.InvalidParams);
        if (envelope.Request.Technology != DemoTechnologies.A2A)
            throw new A2AException("Specialists accept only A2A runs.", A2AErrorCode.InvalidParams);
        return envelope;
    }
}
