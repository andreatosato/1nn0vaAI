# Observatory API

The same executable serves `inline`, `skills`, or `a2a` (`Demo:Technology`).
All three API instances depend on `catalog-service`, `orders-service` and
`returns-service`: three role-configured instances of the same AgentHost executable.
`services.AddObservatoryAgents(configuration)` registers the runtime and HTTP clients.
The API does **not** register local `IShopData` or read the service-domain directory.

Before listening, [RemoteShopCatalog](RemoteShopCatalog.cs) calls
**`GET /catalog` on Catalog** and loads the
complete snapshot, image URLs and provenance for metadata/UI and run snapshots.
This is not a model tool or a local fallback for business requests.
An unavailable/invalid catalog response must not silently become local shop data.
Scenario definitions remain local to Core.

`GET /api/demo-data` is a **read-only synthetic teaching inspector**, not an
agent tool or a customer-authorized order API. Its response is
`{asOf, customerId, notice, orders, policies}`: the frozen `DemoClock.AsOf`
(ISO timestamp with offset), trusted chat customer ID, explicit teaching notice,
all 50 synthetic orders and all 12 policies (highest priority first, including
archived versions). Order `deliveredAt` values remain date-only `yyyy-MM-dd`;
list price and amount paid remain separate.

Every request uses `ShopServiceClient` to GET `/demo-data/orders` from Orders
and `/policies` from Returns over real HTTP, forwarding cancellation. Products
remain available through `/api/products`. The API never generates fixtures,
reads local domain state, invokes models/tools, or creates conversations/runs/drafts.
Unavailable, empty or malformed upstream responses fail with **502 Problem
Details**, code `shop_service_unavailable` and `traceId`, without partial results
or local fallback. Other customers are visible **only for teaching**; chat order
authorization and explicit confirmation guards remain unchanged. Do not expose
this synthetic cross-customer inspector as a production customer-data API.

Targeted real-HTTP regression checks (no model/network-cloud access):

```powershell
dotnet build .\src\Observatory.Api\Checks\Observatory.Api.Checks.csproj
dotnet .\src\Observatory.Api\Checks\bin\Debug\net10.0\Observatory.Api.Checks.dll --demo-data
```

The services, not these APIs, initialize the bundled frozen fixtures/snapshot:
missing catalog files are copied atomically, existing snapshot/domain state is
preserved and invalid snapshots fail startup. Aspire shares `.appdata\catalog`
and `.appdata\domain` only among services; only Orders writes synthetic drafts.
There are no independent shop databases per service. Each API instead owns a
separate SQLite evidence file.

Initialization is offline with respect to external services: the internal HTTP
metadata fetch is real, but no public catalog download, PowerShell invocation,
model call, conversation, run or scenario execution occurs.
Shared Aspire ServiceDefaults provide OTel,
including `Observatory.*` for native framework and model-client spans, and map health routes once.
No Azure resources are created, queried, or inferred from an alias.

## Configuration

Use configuration/environment variables (`:` becomes `__`). Do not put credentials in committed files.

| Key | Meaning/default |
| --- | --- |
| `Demo:Technology` | `inline`, `skills`, `a2a`; default `inline` |
| `Demo:AllowLive` | `false`; authoritative API and runtime opt-in. Explicit `false` overrides legacy root `AllowLive=true` |
| `Demo:DefaultMode` | `live`; the API rejects any other execution mode |
| `AllowLive` | Legacy alias used only if `Demo:AllowLive` is absent |
| `Demo:MaxApprovedBudgetUsd` | Maximum accepted **per-run** explicit budget, default `10` |
| `Storage:Path` | Unique SQLite file per API resource; default `data\observatory-{technology}.sqlite3` under output |
| `Agents:Endpoints:catalog` | Catalog service origin; standalone default `http://localhost:5205` |
| `Agents:Endpoints:orders` | Orders service origin; standalone default `http://localhost:5206` |
| `Agents:Endpoints:returns` | Returns service origin; standalone default `http://localhost:5207` |
| `Agents:AllowRemote` | `false` by default; Aspire enables authenticated remote access in container mode |
| `Agents:SharedSecret` | Backend-only secret shared with all services when remote access is enabled |
| `Processing:Workers` | Concurrent conversations, default `2` |
| `Processing:QueueCapacity` | Bounded scheduler notifications, default `64`; durable jobs remain in SQLite |
| `Processing:MaxPendingRuns` | Durable queue admission limit, default `256` |
| `Processing:RunTimeoutSeconds` | Default `180` |
| `AzureOpenAI:Endpoint` | HTTPS endpoint without credentials or query |
| `AzureOpenAI:ApiKey` | Optional secret, never returned/exported; runtime otherwise uses identity |
| `Models:{profileId}:Deployment` | Actual provisioned deployment; never inferred from a profile alias |
| `Models:{profileId}:ModelVersion`, `Region`, `DeploymentType` | Explicit provenance metadata |
| `Models:{profileId}:InputPerMillion`, `CachedInputPerMillion`, `OutputPerMillion` | Explicit nonnegative rates |
| `Models:{profileId}:CacheWriteSurchargePerMillion` | Optional **legacy additive** cache-write rate; do not combine a positive surcharge with replacement pricing |
| `Models:{profileId}:CacheWritePerMillion` | Optional **replacement** cache-write rate, applied instead of ordinary input pricing |
| `Models:{profileId}:LongContextThresholdTokens` | Optional positive input-token boundary; only strictly larger input selects the long tier for the entire request |
| `Models:{profileId}:LongContextInputPerMillion`, `LongContextCachedInputPerMillion`, `LongContextCacheWritePerMillion`, `LongContextOutputPerMillion` | All four long-tier rates are required, together with the threshold and all four short-tier rates |
| `Models:{profileId}:UseNonReasoningChatTools` | Explicit opt-in to send `reasoning_effort=none` through MEAI; required for Sol function tools on Chat Completions |
| `Models:{profileId}:Currency`, `SourceUrl`, `VerifiedAt`, `Version` | Pricing metadata; `USD`, HTTPS citation, `yyyy-MM-dd`, version |
| `Models:{profileId}:Capabilities:FunctionCalling`, `MaxOutputTokens` | Must explicitly be `true` for LIVE |

Profile IDs: `gpt5`, `gpt6-astra`, `gpt6-sol`, `gpt6-luna`. Pricing keys also accept a
nested `Pricing` section; flat keys take precedence. Unconfigured prices remain null.
AppHost forwards the local `Demo:DefaultMode`, `Demo:AllowLive`,
`Demo:MaxApprovedBudgetUsd`, endpoint and model settings to every backend.
Keep local provider configuration in AppHost user-secrets. The advertised default
is a UI preference, not consent: each LIVE request still
requires explicit mode and positive approved per-run budget. No startup inference
is performed, no draft consent is inferred, and requests must explicitly select LIVE.
Without an API key the existing provider uses `DefaultAzureCredential` with
interactive browser authentication disabled. Use already authenticated Entra
credentials; process-mode Aspire can use the host credential chain.

For local verification, first read `/api/products` and choose an existing product
(the bundled catalog includes product 83, not product 1). Use a fresh conversation
with a read-only catalog question, `confirmAction=false`, `modelProfileId=gpt5`, `mode=live`,
`approvedBudgetUsd=0.10`, `maxModelCalls=3`, and `maxOutputTokens=1024`.
Verify the terminal run's calls have `mode=live`, `usageSource=provider`, actual
input/output tokens and a tool event; test fixtures do not count as evidence of LIVE.
Budgets are checked **between** calls, not a provider-side hard dollar cap;
the output/call limits additionally bound the smoke test.

Official retail verification on 2026-09-25: Azure Retail Prices API
(`https://prices.azure.com/api/retail/prices`), Sweden Central, USD,
Azure OpenAI GPT5, Global Standard, `1M` units: `GPT 5 Inpt Glbl` = **1.25**,
`GPT 5 cchd Inpt Glbl` = **0.125**, `GPT 5 outpt Glbl` = **10**.
Meters respectively `3821dc67-ae8c-55ff-881c-670ba1933db6`,
`d8d5a4d3-4908-58b6-ad10-aa8ff748c8e8`,
`0cfb9629-b1a5-5b77-a654-813c8b6cc246` (effective 2025-08-01).
These are dated retail estimates, not contractual invoice rates. Reverify before
reusing them. Do not substitute GPT5 rates for another model.

GPT6 Sol Global Standard, Sweden Central, USD per **1M**, verified on the same
date (effective 2026-09-01), has the following official Azure retail meters:

| Bucket | Short context | Long context | Short meter ID | Long meter ID |
| --- | ---: | ---: | --- | --- |
| Ordinary input | 2 | 4 | `2e1fcc90-146b-5d51-85f1-68f2a8465118` | `4287ec78-3b1e-5411-898c-6ea5abf8453d` |
| Cached input | 0.2 | 0.4 | `92614c23-374f-55fe-a962-9b3f57c834d9` | `66338950-ffb6-535a-963e-533b84c8a4f6` |
| Cache-write input | 2.5 | 5 | `d87f2196-6de9-55ee-b6b3-7823bd1369d2` | `6848988b-afcb-5196-bd99-9205734af8ab` |
| Output | 10 | 15 | `1519b114-f264-5779-8b5c-61fc691b2d3e` | `ae9f8def-af18-5cda-9eec-3ef4204141cd` |

The [publisher's Sol model documentation](https://developers.openai.com/api/docs/models/gpt-6-sol)
specifies a **272,000 input-token** boundary: exactly 272,000 is short; 272,001 is
long. Output does not affect tier selection, but the selected tier prices the
**whole request**, including output. It also requires `reasoning_effort=none`
for Chat Completions function tools. This is an explicit backend model setting,
not a capability inferred from an alias.

[Provider cache-write pricing](https://developers.openai.com/api/docs/guides/prompt-caching)
replaces ordinary input pricing, rather than adding a surcharge.
[Azure's prompt-caching protocol](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/prompt-caching)
reports `usage.prompt_tokens_details.cache_write_tokens`. The nonstreaming SDK
pipeline captures **only the original usage object**, before typed SDK defaults
can manufacture missing numeric fields as zero. Captures are scoped to each
logical call, including concurrent calls; full wire requests/responses are not
logged. Missing or malformed input/output/cache-read counts leave cost unknown;
replacement/tiered cards also require an explicit cache-write count, including
an explicit zero. Missing usage blocks subsequent LIVE calls, not a zero-cost
fallback. Inconsistent/overlapping input buckets and incomplete long-tier cards
also fail closed. Both runtime spending and persisted ledgers use the same Core
calculator and retain the selected `pricingTier` and frozen rate card:

```text
cost = ((input - cached - writes) * inputRate
        + cached * cachedRate + writes * writeRate + output * outputRate) / 1,000,000
```

Reasoning tokens are an output subset and are never added again. Legacy additive
surcharge snapshots retain their original accounting. Astra/Luna remain
unpriced/fail-closed unless separately configured with verified complete cards.
The same bounded read-only smoke above may select `gpt6-sol` only after these
rates, provenance, explicit capabilities, and nonreasoning tools are configured.

### Explicit unbounded measurements

Set `Demo:AllowUnboundedExecution=true` on AppHost (propagated to all APIs and
specialists) and send `configuration.unboundedExecution=true` with
`approvedBudgetUsd=null` for an explicitly authorized measurement. This opt-in
removes the application spending/call caps and run timeout, omits the provider
output-token cap, and removes the A2A invocation HTTP timeout. The legacy numeric
output/call fields are retained for compatibility but are ignored for these runs.
The SDK iteration property is set to its technical maximum (`int.MaxValue`);
provider context, output, quota and transport constraints still apply.

Default bounded runs are unchanged. The flag is frozen and exported with the run,
propagates to remote specialists, and is shown in the UI. Model readiness, verified
pricing, usage capture, explicit LIVE consent, cancellation, ownership and action
confirmation remain mandatory. Missing usage still stops execution; no fake
zero-cost fallback is introduced. Never enable this on a public anonymous service.

Offline verification (new database path required):
`dotnet run --project src\Observatory.Api -- --self-test --unbounded <new-database-path>`

`GET /api/config` returns frozen `DemoConfiguration` fields plus a `capabilities` object
and a `promptBlocks` catalog (`id`, `label`, `description`).
It advertises direct tools (not MCP), full/compact history and technology-specific
`capabilities.agentNames`:

- Inline and Skills: `["router"]`; only router model overrides apply.
- A2A: `["router","catalog","orders","returns"]`; remote specialist overrides also apply.

UI controls must follow these capabilities, not assume four agents in every mode.
The ledger still records only actual model calls, not every advertised agent.
`ToolTransport=direct` means ordinary framework function invocation rather than
MCP, **not** business logic in the API process.

Aspire supplies three dynamic service origins and waits for all services in
both process/container modes. Origins must not contain `/api`, `/a2a`,
credentials, query or fragment. `Agents:BaseUrl` is no longer used.
Standalone API launch profiles use the three localhost defaults above.
API instances keep separate `.appdata\{technology}\observatory.sqlite` files;
containers mount only their own `/state/{technology}` evidence directory.

## Runtime and backend service boundary

Inline has one router with inline procedures and ordinary HTTP business tools.
Skills has the same router/tools plus the native provider reading trusted
bundled copies of `shop-catalog`, `shop-orders`, `shop-returns`, versioned
with the same packages exposed by the services. There is no `shop-router`,
dynamic skill download, script execution or delegation to specialist agents.

A2A has a local router delegating to each role's actual remote agent/card at
`/a2a/{role}` on its own origin. Specialists have their own model/tool loops
and use local Core operations in the service. No native skill provider is
loaded in A2A; `AgentCard.Skills` is protocol capability metadata.

Business tools call the [role-scoped service APIs](..\Observatory.AgentHost\README.md):
Catalog `/products`, `/catalog/query` and `/catalog/facets`,
Orders `/orders` and `/return-drafts`, Returns `/policies`
and `/return-assessments`. The full `/catalog` snapshot is reserved for
startup metadata/UI, never passed as an AI tool result.

The backend derives `X-Observatory-Customer-Id` from trusted customer scope and
sets `X-Observatory-Confirm-Action=true` only for explicitly authorized draft
creation. These are not forwarded UI service headers or model tool arguments;
chat text cannot grant consent. The domain service rechecks scope and eligibility.
In A2A the trusted invocation metadata carries customer/consent context.

When remote access is enabled, the HTTP clients send `X-Observatory-A2A-Key`
for all service non-health endpoints: business APIs, skills, discovery, A2A,
telemetry and index. The generated key is backend-only and is never part of
UI configuration, model context or exported evidence. All local calls remain
real HTTP independently of the model provider.

## HTTP and evidence

`POST /api/prompts/preview` accepts a `RunConfiguration` and returns
`{technology, agents: [{agent, instructions, characterCount}], notice}`.
It is a pure preview, returning 200 without creating evidence or calling
models, tools, services or a native skill provider. It validates ordinary
configuration shape/options but does not require LIVE authorization, budget or
deployment readiness. **Run submission and execution retain all LIVE gates.**
Characters are UTF-16 code units, not model token counts.

The optional `promptBlocks` object contains boolean `checklist`, `outputContract`,
`examples`, `redundancy`, `conflictingStyle` switches. Omitted properties default
to false; omitted objects in older persisted runs do too. Null is rejected (400),
and unknown, duplicate or nonboolean properties use the existing JSON errors (400).
Selected blocks are part of the persisted run snapshot and A2A configuration.
All-disabled selections retain pre-laboratory idempotency hashes; enabling a
block changes request identity and requires a new idempotency key.
They supplement, never replace, mandatory factual/authorization instructions.
Previewed instructions come from the execution compositor. Runtime history,
tools, native skill context and results are not available until execution;
the Inspector remains authoritative for actual inference requests.
Manual examples are in [Observatory.Api.http](Observatory.Api.http).

Model, prompt profile, optional blocks and history strategy are frozen when
a turn is accepted. Updating controls or generating a preview does not mutate
an active/queued/completed run or create a conversation. The next accepted
turn uses its newly submitted configuration, including per-agent overrides;
runtime agents/clients are created for that run, with no restart required.
The same conversation retains earlier turns. Use a new conversation when
comparing model/prompt configurations without previous-answer influence.

Routes follow the shared `/api` UI contract. `/health` and `/alive` are always mapped.
POST conversation returns 201. POST turn returns 202 with `{runId,conversationId,eventsUrl}`.
`Idempotency-Key` header is optional; if both header/body specify it they must agree.
Same key/payload reuses the original run even after restart; differing payload returns 409.
IDs are 32 hexadecimal characters. Unknown JSON fields/options are rejected with ProblemDetails.

SQLite transactions persist exact submitted request/message, model/pricing/catalog snapshot,
conversation messages, statuses, idempotency keys, the event timeline and a **separate** model-call ledger.
FIFO is enforced per conversation; different conversations can run concurrently.
Queue notifications may coalesce; jobs cannot disappear when the in-memory channel is full.
Cancellation and accounting limits signal the runtime cancellation token but never abort the
event sink: already-completed remote calls continue draining into the ledger before the run becomes terminal.
Provider failures retain their own failure reason instead of being replaced by a missing-usage error.
An ownership lock rejects sharing a database between simultaneously running API processes.
After restart, interrupted jobs and queued LIVE jobs fail explicitly instead
of repeating possibly paid or side-effecting work.

`GET /api/runs/{id}/events` uses default SSE `message` events with numeric sequence IDs.
`Last-Event-ID` accepts a stored sequence or event ID belonging to that run.
`DELETE /api/runs` clears this demo's terminal run history, model-call/event records,
idempotency entries and experiment reports. Conversation titles and message text remain,
with references to deleted runs removed. It returns `{deletedRuns}` and responds with 409
while any run or experiment is active.
`POST /api/runs/{id}/replay` returns the original `RunRecord` as JSON by default,
with extra `replayOnly`, `originalRunId`, `replayThroughSequence` and `notice` metadata.
An explicit `Accept: text/event-stream` instead streams only the original events stored
at request time. Both forms include `X-Original-Run-Id` and `X-Replay-Only: true`;
the SSE form also includes a snapshot sequence header. Neither form creates a run.
Exports are downloadable sanitized JSON: exact request provenance (with secrets redacted),
public catalog source/hash/retrieval/product count and image policy, separate ledger and timeline.
Images are UI-only. Runtime history retains server-stored product IDs and
source references so follow-ups can resolve previously shown products.
These records contain no image fields; full-history prompt messages use
their text. Tool facts are reread for current prices and availability, not
assumed from earlier answers.

Only provider usage is priced. Cached input is subtracted
from ordinary input; reasoning remains a subset of output; cache-write has a separate surcharge.
Missing usage/rates stays partial/unpriced, never an invented zero. Trace data is not summed with ledger data.
Single LIVE runs require all settings, deployment/capability/rate verification and an explicit
positive `ApprovedBudgetUsd`; runtime limits and post-call budget checks stop additional calls.
They are **not** an absolute provider-side spending cap or a paid-batch authorization.
Paid experiments are intentionally rejected: `ExperimentRequest` has no aggregate budget approval.

`POST /api/experiments` defaults to dry-run and estimates configured **maximum** call count
without executing anything. Executed experiments are disabled; tests use fixture runtimes
and transparent expected-fact checks, without model ranking or fabricated cost/usage.
Results are persisted and accessible at `GET /api/experiments/{id}`.

## Verification

The commands below are verification entry points, not a claim that the new
three-service architecture has passed them. Rebuild before running old outputs.

Build with cached packages: `dotnet build src\Observatory.Api --ignore-failed-sources`.
Run the local no-network self-check with a new explicit project-local SQLite path:

```powershell
dotnet run --no-build --project src\Observatory.Api -- --self-test src\Observatory.Api\.checks\check.sqlite3
```

The self-check uses test-only fixtures; production DI always uses Observatory.Agents
and the remote Catalog metadata path. Fixture results are not proof of live service routing.
Budget/cancellation drain checks simulate pre-captured provider batches locally; no inference SDK or network is invoked.
It can also build independently of the agent runtime:

```powershell
dotnet run --project src\Observatory.Api\Checks -- src\Observatory.Api\.checks\independent.sqlite3
```

The same test executable has an explicit `--serve-fixture` mode for local HTTP/Runner tests
while other projects are being developed. It requires `--Storage:Path`, always forces
`Demo:AllowLive=false`, and registers a clearly labeled test fixture instead of production agents.
Do not use fixture results as validation of actual agent/model behavior.

For actual HTTP and SSE validation use Observatory.Runner `smoke`
with all three services running. Verify router-only Inline/Skills, actual
remote specialists for A2A, Catalog-dependent startup, role-specific origins,
backend authorization and separate SQLite evidence. Previous four-agent
results for every mode must not be reused as evidence for this architecture.
