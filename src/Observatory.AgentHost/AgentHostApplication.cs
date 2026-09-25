using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2A;
using A2A.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Observatory.Agents;
using Observatory.Core;

namespace Observatory.AgentHost;

public static class AgentHostApplication
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Configure(builder);
        return builder;
    }

    public static void Configure(WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetValue<bool>("Agents:AllowRemote"))
            builder.Configuration["AllowedHosts"] = "*";
        builder.AddServiceDefaults();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
        builder.Services.AddSingleton<IShopData>(_ => new ShopData(
            builder.Configuration["Data:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "data"),
            builder.Configuration["Shop:StatePath"] ?? builder.Configuration["Data:StateDirectory"]));
        builder.Services.AddObservatoryAgents(builder.Configuration);
        builder.Services.AddSingleton<RemoteTelemetryStore>();
    }

    public static WebApplication Build(WebApplicationBuilder builder, string role, Action<WebApplication> mapBusiness)
    {
        if (!AgentNames.Specialists.Contains(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        var app = builder.Build();
        var access = app.Services.GetRequiredService<AgentTransportAccess>();
        var shopData = app.Services.GetRequiredService<IShopData>();
        app.Logger.LogInformation(
            "{Service} demo data initialized: {ProductCount} products, {PolicyCount} synthetic policies and synthetic orders. Catalog hash: {CatalogHash}.",
            role, shopData.Products.Count, shopData.Policies.Count, shopData.Catalog.ContentHash);
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            ExceptionHandler = async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var (status, code, detail) = error switch
                {
                    DomainException domain => (BusinessRequestValidation.StatusFor(domain.Code), domain.Code, access.Redact(domain.Message)),
                    Microsoft.AspNetCore.Http.BadHttpRequestException bad => (bad.StatusCode, "invalid_request", "Richiesta non valida o troppo grande."),
                    _ => (500, "service_error", "Operazione del servizio non riuscita. Nessun fallback; consultare i log backend.")
                };
                await Results.Problem(statusCode: status, title: code, detail: detail,
                    extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
            }
        });
        app.Use((HttpContext context, RequestDelegate next) => EnforceTransportAccessAsync(context, next, access, app.Logger));
        app.UseStatusCodePages(async context =>
        {
            await Results.Problem(statusCode: context.HttpContext.Response.StatusCode,
                title: "http_error", detail: "Operazione non disponibile su questo servizio.").ExecuteAsync(context.HttpContext);
        });
        app.MapDefaultEndpoints();
        app.MapGet("/", () => Results.Ok(new
        {
            service = app.Environment.ApplicationName,
            role,
            mode = "live only; explicit budget or server-authorized unbounded execution",
            access = access.AllowRemote ? "authenticated-backend-network" : "loopback-only",
            a2a = new { endpoint = $"/a2a/{role}", card = $"/a2a/{role}/.well-known/agent-card.json" },
            skill = new { name = $"shop-{role}", document = $"/skills/shop-{role}/SKILL.md" },
            telemetry = "/telemetry/{runId}?invocationId={invocationId}",
            mcp = new { supported = false, reason = "Optional MCP transport is not implemented in this release." }
        }));
        app.MapGet("/telemetry/{runId}", (string runId, string? invocationId, RemoteTelemetryStore store) =>
        {
            if (invocationId is null) return Results.Ok(store.ReadRun(runId));
            var batch = store.Read(runId, invocationId);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        });

        mapBusiness(app);
        var taskManager = CreateTaskManager(app.Services, role);
        app.MapA2A(taskManager, $"/a2a/{role}");
        app.MapGroup($"/a2a/{role}").MapWellKnownAgentCard(taskManager, $"/a2a/{role}");
        return app;
    }

    internal static async Task EnforceTransportAccessAsync(HttpContext context, RequestDelegate next,
        AgentTransportAccess access, ILogger logger)
    {
        var health = context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Equals("/alive", StringComparison.OrdinalIgnoreCase);
        if (health && HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }
        var headers = context.Request.Headers[AgentTransportAccess.HeaderName];
        var key = headers.Count == 1 ? headers[0] : null;
        if (!access.IsAuthorized(context.Connection.RemoteIpAddress, key))
        {
            logger.LogWarning("Rejected unauthorized request to the agent transport.");
            context.Response.StatusCode = access.AllowRemote ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
            if (access.AllowRemote) context.Response.Headers.WWWAuthenticate = "ApiKey realm=\"Observatory.AgentHost\"";
            await context.Response.WriteAsJsonAsync(new
            {
                error = access.AllowRemote ? "a2a_authentication_required" : "loopback_required"
            });
            return;
        }
        if (health)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET";
            return;
        }
        await next(context);
    }

    private static TaskManager CreateTaskManager(IServiceProvider services, string role)
    {
        var access = services.GetRequiredService<AgentTransportAccess>();
        var manager = new TaskManager();
        manager.OnAgentCardQuery = (serverUrl, _) => Task.FromResult(new AgentCard
        {
            Name = role,
            Description = $"Specialista {role} del negozio, realizzato con ChatClientAgent di Microsoft Agent Framework. Restituisce solo dati di dominio verificati.",
            Url = new Uri(new Uri(serverUrl), $"/a2a/{role}").AbsoluteUri,
            Version = "1.0.0",
            ProtocolVersion = "0.3.0",
            Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["application/json"],
            SecuritySchemes = access.AllowRemote ? new()
            {
                [AgentTransportAccess.SecuritySchemeName] = new ApiKeySecurityScheme(
                    AgentTransportAccess.HeaderName, "header", "Backend-to-backend shared key; configured out of band.")
            } : null,
            Security = access.AllowRemote ? [new() { [AgentTransportAccess.SecuritySchemeName] = [] }] : null,
            Skills =
            [
                new AgentSkill
                {
                    Id = $"shop-{role}",
                    Name = $"Shop {role}",
                    Description = role == AgentNames.Catalog
                        ? "Ricerca prodotti, conteggia modelli e pezzi su filtri combinati, leggi dettagli, categorie e colori testuali del catalogo."
                        : $"Esegue lo specialista {role} con configurazione isolata della run, identita cliente vincolata al server e strumenti autorevoli del negozio.",
                    Tags = ["shop", role, "synthetic-demo"]
                }
            ]
        });
        manager.OnMessageReceived = async (message, token) =>
        {
            if (message.Metadata?.TryGetValue(A2ATransport.MetadataKey, out var json) != true)
                throw new A2AException($"Missing standard A2A metadata extension '{A2ATransport.MetadataKey}'.", A2AErrorCode.InvalidParams);
            RemoteInvocation envelope;
            try
            {
                envelope = json.Deserialize<RemoteInvocation>(AgentJson.Options)
                    ?? throw new JsonException("Empty run metadata.");
            }
            catch (JsonException error)
            {
                throw new A2AException("Invalid per-run metadata.", error, A2AErrorCode.InvalidParams);
            }
            if (!Guid.TryParseExact(envelope.InvocationId, "N", out _)
                || envelope.Request is null || message.Message.Role != MessageRole.User
                || message.Message.MessageId != envelope.InvocationId)
                throw new A2AException("Invalid invocation correlation.", A2AErrorCode.InvalidParams);
            var query = string.Join("\n", message.Message.Parts.OfType<TextPart>().Select(part => part.Text));
            if (string.IsNullOrWhiteSpace(query) || message.Message.Parts.Any(part => part is not TextPart))
                throw new A2AException("Only domain text messages are supported.", A2AErrorCode.ContentTypeNotSupported);
            if (envelope.Request.Technology != DemoTechnologies.A2A)
                throw new A2AException("Specialists accept only A2A requests.", A2AErrorCode.InvalidParams);
            var runtime = services.GetRequiredService<ISpecialistAgent>();
            var store = services.GetRequiredService<RemoteTelemetryStore>();
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(role + "\n" + query + "\n" + json.GetRawText())));
            var result = await store.ExecuteAsync(envelope.Request.RunId, envelope.InvocationId, fingerprint,
                emit => runtime.ExecuteAsync(envelope.Request, query, emit, token)).ConfigureAwait(false);
            return new AgentMessage
            {
                Role = MessageRole.Agent,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = message.Message.ContextId,
                Parts = [new TextPart { Text = JsonSerializer.Serialize(result, AgentJson.Options) }]
            };
        };
        return manager;
    }
}
