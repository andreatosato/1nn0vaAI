# Observatory.SpecialistHost

Shared host support for the three A2A specialist agent processes:
`agent-catalog`, `agent-orders`, `agent-returns`. Each process owns one
specialist role (`catalog`, `orders` or `returns`), its own model client, prompt
and tool loop, and exposes only that role's A2A surface and prompt preview.
Business APIs are separate `shop-*` processes. Agent Skills are published by
separate `skill-*` processes.

Inline and Skills call the business HTTP endpoints without specialist
inference. A2A invokes real remote agents, each with its own model/tool loop.
The default access policy is loopback-only; private container networks require
explicit authenticated remote access.

The host references Core, Agents and ServiceDefaults and uses the official
`A2A.AspNetCore` **0.3.4-preview** server with the matching **0.3.4-preview**
client. Framework agents are created per invocation; request-specific
model/prompt/history/customer settings are never global mutable configuration.

## Run

The normal entry point, from the workspace root, is one Aspire AppHost:

```powershell
dotnet run --project .\src\Observatory.AppHost --launch-profile http
```

It supplies dynamic shop service origins to routers and agents through
`Agents:Endpoints:catalog`, `Agents:Endpoints:orders`,
`Agents:Endpoints:returns`. `Agents:BaseUrl` is no longer the routing contract.
Each value is an HTTP(S) **origin**, without `/api`, `/a2a`, credentials,
query or fragment.

For standalone development, build the three agent projects, then run **one
command per terminal**:

```powershell
dotnet build .\src\Agents\Observatory.Agent.Catalog\Observatory.Agent.Catalog.csproj
dotnet build .\src\Agents\Observatory.Agent.Orders\Observatory.Agent.Orders.csproj
dotnet build .\src\Agents\Observatory.Agent.Returns\Observatory.Agent.Returns.csproj
```

```powershell
dotnet run --no-build --project .\src\Agents\Observatory.Agent.Catalog --launch-profile http
dotnet run --no-build --project .\src\Agents\Observatory.Agent.Orders --launch-profile http
dotnet run --no-build --project .\src\Agents\Observatory.Agent.Returns --launch-profile http
```

| Project | Standalone origin | Aspire resource |
| --- | --- | --- |
| `Observatory.Agent.Catalog` | `http://localhost:5311` | `agent-catalog` |
| `Observatory.Agent.Orders` | `http://localhost:5312` | `agent-orders` |
| `Observatory.Agent.Returns` | `http://localhost:5313` | `agent-returns` |

`--urls`/`ASPNETCORE_URLS` can override the listener. Use Aspire for the
shop API origins, skill sites and shared persistent catalog/domain paths
described below.

## Role-scoped HTTP surface

Paths are relative to the selected agent origin, not the demo API prefix.
Business endpoints are mapped by the `shop-*` projects, not by this host.
Specialist tools call those APIs over HTTP through the role-owned `Tools`
folder.

Each role also exposes:

| Endpoint | Purpose |
| --- | --- |
| `GET /health`, `GET /alive` | Shared ServiceDefaults readiness/liveness |
| `POST /a2a/{role}` | Official A2A `message/send` JSON-RPC handler for the configured role |
| `GET /a2a/{role}/.well-known/agent-card.json` | That role's official agent card |
| `GET /telemetry/{runId}?invocationId={id}` | Separate internal invocation telemetry batch |
| `GET /telemetry/{runId}` | Retained batches for the run on this service |
| `GET /` | Service endpoint/capability index |

For example, the Catalog agent origin does not host `/a2a/orders`.
There is no `shop-router` skill. The Skills router reads remote Markdown from
the dedicated `skill-catalog`, `skill-orders` and `skill-returns` sites; those
native Markdown skills are distinct from
`AgentCard.Skills`, which describes A2A protocol capabilities. A2A agents use
inline prompts and do **not** load the native skill provider.

### Identity, confirmation and A2A messages

The trusted backend sets `X-Observatory-Customer-Id` from its customer context.
For draft creation it sets `X-Observatory-Confirm-Action=true` only after
explicit validated user consent. Neither value is a model-visible tool
argument, a field of `{orderId,reason}`, or a browser-supplied service header.
Text claiming confirmation cannot authorize an action or change customers.
Only Orders writes drafts; drafts do not issue payments or refunds.

A2A's standard `MessageSendParams.Metadata["observatory.run"]` extension
carries `RemoteInvocation`: invocation ID and frozen run request, including
trusted customer/consent context. `MessageId` must match the invocation ID.
Model input remains domain text/history, not the transport envelope.
Outputs are standard A2A messages containing a domain `AgentExecutionResult`;
telemetry is retrieved separately, never included in message parts.
The SDK's well-known card route is mapped within the configured role's group.

## Remote access opt-in

| Configuration | Behavior |
| --- | --- |
| `Agents:AllowRemote=false` (default) | Loopback peers/endpoints only, even if a key is supplied |
| `Agents:AllowRemote=true` | Requires a valid `Agents:SharedSecret` at startup |
| `Agents:SharedSecret` | Same backend-only key on all service and API instances; 32-256 visible ASCII characters, no whitespace |
| `X-Observatory-A2A-Key` | Required on every non-health endpoint in remote mode, including business APIs, skill sites, discovery, A2A, telemetry and root |

Use a cryptographically random secret; Aspire generates one per container
session and passes it only to backends. Environment names are
`Agents__AllowRemote`, `Agents__SharedSecret` and, on callers,
`Agents__Endpoints__catalog`, `Agents__Endpoints__orders`,
`Agents__Endpoints__returns`. Never put the key in the frontend, URLs, model
context, A2A metadata or committed settings. Cards advertise the header's
security scheme, not the secret.

Missing, incorrect or duplicate keys receive HTTP **401** in remote mode,
including from loopback callers/proxies: there is no local anonymous bypass.
The transport guard uses fixed-time comparison of SHA-256 key hashes.
External proxies/custom HTTP logging must not capture credential headers.
Identity and confirmation headers are not substitutes for this key.

Only **`GET /health` and `GET /alive`** bypass access checks. Other methods
still require access and are rejected with 405 after successful authorization.
Container DNS Host headers are accepted when remote mode is enabled; the
default retains local Host restrictions. The .NET SDK container configuration
supplies `ASPNETCORE_URLS=http://+:8080`; Aspire assigns external endpoints.

Backend clients disable redirects, forward proxies and automatic cookies.
HTTP is intended for a trusted private container network; use HTTPS for
untrusted networks. This shared-key integration is not public-user or
multi-tenant authentication. Rotate keys by restarting all backend instances.
Streaming/background tasks/push notifications are not advertised; MCP is
not implemented.

## Initialization and persistence

Each service initializes Core's `ShopData` before listening.
`Data:Directory` defaults to `AppContext.BaseDirectory\data`; Core copies the
bundled catalog transitively at build/publish. Aspire supplies
`.appdata\catalog` (`/state/catalog` in containers) to all three services.
If missing, the snapshot is validated and copied atomically; concurrent
instances reuse the winning copy. Existing snapshots are not replaced, and
invalid snapshots fail startup. Acquisition metadata/hash and drafts remain
intact. No duplicate catalog-copy rule is needed in this project.

The services share the same frozen Core fixtures and snapshot, not
independent shop databases. Only Orders writes synthetic drafts to the
shared persistent `Shop:StatePath` directory: `.appdata\domain` or
`/state/domain`. `Data:StateDirectory` is a legacy fallback.
`Storage:Path` belongs to each API's separate evidence SQLite file and is
not a service-domain directory.

Demo APIs have no local `IShopData`. Before listening, each loads catalog,
image URLs and provenance with `GET /catalog` from Catalog for metadata/UI.
Scenario definitions remain local to Core. Initialization creates no
conversation/run, executes no scenario and calls no model, seed script or
external catalog API. The internal Catalog HTTP request is still real.

The three agent projects share this host plumbing but run as distinct
processes.
AppHost preserves model/provider settings and keeps LIVE disabled by default.
Per-invocation A2A model/prompt/history settings stay isolated.

## Telemetry and verification

Run/invocation IDs partition each service's in-memory `RemoteTelemetryStore`.
Exact invocation replay shares the original execution rather than repeating
write tools; changing metadata/input for that ID is rejected. Completed
telemetry has a 30-minute retention and a maximum of 512 retained invocations.
Missing/expired telemetry is not silently treated as complete. The calling
API imports and persists remote evidence in its own SQLite ledger.

Shared ServiceDefaults maps health endpoints once and subscribes to
`Observatory.*`; no extra global HTTP retry policy is installed here.

Acceptance checks for the role-separated architecture include:

- Three distinct loopback agent instances and only role-owned A2A routes/cards.
- Router-only Inline/Skills with real business HTTP; remote specialist loops in A2A.
- Native skill discovery/body/resource loading only in Skills, from the `skill-*` sites.
- Identity/confirmation enforcement, image-free AI facts and Orders-only draft writes.
- Shared snapshot/domain persistence, startup without runs and API metadata via Catalog.
- Authenticated business, skill, card, A2A and telemetry requests in remote mode.
- Correlated per-call evidence without secrets, fabricated usage/cost or double counting.
- Complete catalog counts, stock totals, Italian filter follow-ups and bounded examples.
- Explicit numeric/filter errors and malformed catalog responses without successful-looking fallback.

These are expectations, not a report that the new architecture has passed
the suites. Rebuild before using `--no-build`; previous host-singleton results
do not validate three-service routing. No Azure inference, provider token
semantics, deployment compatibility or model benchmark is claimed.
See [Agents README](..\Observatory.Agents\README.md) for provider gates and the
between-call budget limitation.
