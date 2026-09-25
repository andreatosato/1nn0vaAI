# Shared observability

`AddServiceDefaults()` configures the standard Aspire OpenTelemetry pipeline:
structured logs and scopes; ASP.NET Core, HttpClient and runtime metrics;
ASP.NET Core/HttpClient distributed traces; health probes and service discovery.
The health endpoints are excluded from request tracing. OTLP export is enabled
when Aspire supplies `OTEL_EXPORTER_OTLP_ENDPOINT`; no exporter is required for
offline operation.

`UseObservatoryTelemetry()` configures the **official** instrumented
`ChatClientBuilder` and `AIAgentBuilder` wrappers with sensitive content disabled.
There are no custom AI span builders, token meters or telemetry exporters.
Both ActivitySources **and** Meters are registered:

| SDK | Source/meter name |
| --- | --- |
| Microsoft.Extensions.AI | `Experimental.Microsoft.Extensions.AI` |
| Microsoft Agent Framework | `Experimental.Microsoft.Agents.AI` |

The explicit names match the shared instrumentation configuration. Existing
`Observatory.*` application activities remain subscribed for request correlation.
Model spans are children of agent spans; native token and duration measurements
are visible in Aspire. Logs use `Microsoft.Extensions.Logging` and the same OTLP
pipeline. Native span/metric schemas are experimental SDK contracts.

## Inspector evidence is application data

`Observatory.Agents\Evidence` owns sanitized logical requests/responses,
provider usage extraction, priced token ledgers and run accounting.
Remote protected evidence exchange and API SQLite persistence support Inspector
and comparisons; they are **not** an alternative OTLP collector or span store.
Trace/span IDs only correlate these records with native SDK spans.
Missing sampling/export does not prevent evidence capture. Missing provider
usage stays unknown; it must not be replaced with a telemetry estimate.

Evidence depends on Core application contracts and stays in Agents. ServiceDefaults
depends only on platform/SDK packages, never Core or Agents, avoiding a cycle.
Provider auth, SDK retry behavior, credential sanitization and execution policy
remain in their existing application layer.

## Offline verification

`dotnet test tests\Observatory.Telemetry.Tests\Observatory.Telemetry.Tests.csproj`
uses a local deterministic `IChatClient` with no HTTP, Azure or paid inference.
Activity/Meter listeners verify native agent/chat spans, parentage, token/duration
measurements and absence of prompt/response content. A separate host-level check
verifies the ServiceDefaults source and meter subscriptions with in-memory
processors/exporters, without an OTLP endpoint.

References:
- [Agent Framework observability](https://learn.microsoft.com/agent-framework/agents/observability)
- [OpenTelemetryChatClient](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient)
- [Aspire telemetry](https://learn.microsoft.com/dotnet/aspire/fundamentals/telemetry)
