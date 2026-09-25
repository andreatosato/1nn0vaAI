# Observatory agent runtime

`services.AddObservatoryAgents(configuration)` registers `IAgentRuntime`,
`ShopServiceClient` and the A2A transport. API callers do **not** register local
`IShopData`: business calls go to three separately hosted services.
Each AgentHost instance selects one `Agents:Role` and supplies its local Core
`IShopData` only to that role's business operations and remote specialist.
The instances share frozen fixtures/snapshot, not independent shop databases.

## Three real framework paths

All modes use Microsoft Agent Framework `ChatClientAgent` and its actual function-invocation loop:

- **inline:** one router, with domain procedures in its inline prompt.
  `ShopFunctions` exposes ordinary function tools; `ShopServiceClient` calls
  Catalog, Orders and Returns over real business HTTP. No in-process
  specialist agents or agent-as-tool delegation are created.
- **skills:** the same single router and business HTTP tools. Its native
  `AgentSkillsProviderBuilder`/`AgentSkillsProvider` discovers three trusted
  bundled service packages. Actual `load_skill` calls load `SKILL.md`;
  `read_skill_resource` progressively reads the Returns checklist.
  Loading instructions does not delegate to another agent.
- **a2a:** the router's delegation tools use the official **A2A SDK JSON-RPC**
  client, selecting a distinct origin for each specialist. Three
  role-configured AgentHost instances create their own real framework agents,
  model clients and tool loops per invocation. Remote tools use local Core
  operations within the owning service. A2A uses inline prompts and does
  **not** attach a native skill provider. It is not custom REST labeled A2A.

`AgentNames.ForTechnology` and API `capabilities.agentNames` distinguish
`["router"]` for Inline/Skills from
`["router","catalog","orders","returns"]` for A2A. Only invoked agents appear
in the ledger. The A2A comparison therefore changes agent execution as well
as transport; it is not a network-only benchmark at equal inference counts.

### Native skill packages

The router's provider reads local **bundled, trusted and versioned copies**
of the same packages exposed by the three services:

| Package | Service document |
| --- | --- |
| `shop-catalog` | Catalog: `/skills/shop-catalog/SKILL.md` |
| `shop-orders` | Orders: `/skills/shop-orders/SKILL.md` |
| `shop-returns` | Returns: `/skills/shop-returns/SKILL.md` |
| Returns resource | Returns: `/skills/shop-returns/references/decision-checklist.md` |

There is no `shop-router` skill. The source directory defaults to
`AppContext.BaseDirectory\Skills` (`Agents:SkillsDirectory` is an operator
override for trusted files). The provider does not download Markdown during
the run. Script discovery/execution is disabled; only Markdown resources are
allowed. Procedure text is not an authoritative catalog/order/policy source.
Descriptions, procedures, examples and the Returns reference checklist in
these application-owned Markdown packages are in Italian. Skill names,
resource paths, tool identifiers and JSON keys keep their stable wire values.
`AgentCard.Skills` is A2A capability metadata, not these Markdown documents
or a request to load the native provider.

Azure OpenAI is the sole inference provider. Each configured role creates an
`IChatClient` for its selected deployment; unavailable deployments, invalid
capabilities, provider errors and A2A failures remain explicit errors, with no
alternate model or provider fallback.

### Service origins and access for processes or containers

The API/runtime routes business HTTP and A2A by role:

| Configuration | Standalone default | Aspire resource |
| --- | --- | --- |
| `Agents:Endpoints:catalog` | `http://localhost:5205` | `catalog-service` |
| `Agents:Endpoints:orders` | `http://localhost:5206` | `orders-service` |
| `Agents:Endpoints:returns` | `http://localhost:5207` | `returns-service` |

Aspire supplies dynamic endpoints. These are origins without credentials,
paths, query or fragment; the former `Agents:BaseUrl` is not used.
Each service maps only its role's API, `/a2a/{role}`, agent card and skill.
All demo APIs depend on all three services, not just the A2A demo.

Default: `Agents:AllowRemote=false`, so only loopback endpoints/peers are allowed.

For a private backend/container network, explicitly set **both** `Agents:AllowRemote=true`
and `Agents:SharedSecret` on all API and service instances. Use the same
cryptographically random secret (recommended: 32 random bytes encoded as 64
hex characters). Validation requires 32-256 visible ASCII characters without
spaces and fails startup if remote mode has no valid key. Restart all
backends to rotate the key.

The key is sent **only** in HTTP header **`X-Observatory-A2A-Key`**.
Despite its historical name, it protects **all** non-health service endpoints:
business APIs, skill documents, official discovery, A2A JSON-RPC, separate
telemetry and the index. Only `GET /health` and `GET /alive` bypass the guard.
It is never placed in run metadata, tool results, model context or the ledger.
Backend HTTP clients disable redirects, forward proxies and automatic cookies;
discovery must advertise the exact configured role endpoint. Remote mode
requires authentication even from loopback callers/proxies.

Environment names: `Agents__AllowRemote`, `Agents__SharedSecret` and
`Agents__Endpoints__catalog`, `Agents__Endpoints__orders`,
`Agents__Endpoints__returns`. Pass the secret through Aspire secret parameters
or another backend secret source, **never to the frontend or committed settings**.
HTTP is intended only for a trusted private container network; use HTTPS for
untrusted networks. Do not capture credential headers in proxies/custom logs.

### Business HTTP, metadata and shared state

`ShopFunctions` exposes the same domain tool names in Inline/Skills that the
owning specialist uses locally in A2A:

| Tool | Business HTTP endpoint for Inline/Skills |
| --- | --- |
| `query_catalog` | Catalog `GET /catalog/query?query=&category=&color=&maxPrice=&inStockOnly=&take=` |
| `get_catalog_facets` | Catalog `GET /catalog/facets` |
| `search_products` | Catalog `GET /products?query=&maxPrice=&take=` |
| `get_product` | Catalog `GET /products/{productId:int}` |
| `get_order` | Orders `GET /orders/{orderId}` |
| `create_return_draft` | Orders `POST /return-drafts` with `{orderId,reason}` |
| `get_policies` | Returns `GET /policies` |
| `assess_return` | Returns `POST /return-assessments` with `{orderId,reason}` |

`GET /catalog` is different: API startup uses the full snapshot, including
images/provenance, for metadata/UI **before listening**. It is not an AI tool.
Scenarios remain local definitions; startup never creates runs/conversations
or executes models. Services initialize the bundled snapshot offline and
atomically only if missing, preserving existing snapshots and domain state.
Only Orders writes drafts to the shared persistent `.appdata\domain`
(`/state/domain`); evidence SQLite files belong separately to the demo APIs.
HTTP/authentication failures have no local-domain or alternate-response fallback.

### Conversational catalog queries

`query_catalog(query?, category?, color?, maxPrice?, inStockOnly=false, take=5)`
returns the applied `filters`, `totalProducts`, `inStockProducts`, `stockUnits`,
`products`, `hasMore` and `colorBasis`. Counts cover **all matching products
before `take`**; `take` limits only the returned examples, not the totals.
`stockUnits` is the sum of available pieces, not the count of distinct products.
`hasMore` distinguishes a partial page from the full matching set.

`get_catalog_facets()` returns `totalProducts`, `categories` and `colors`;
each category/color entry has `value`, `productCount` and `stockUnits`, with
`colorBasis` describing the textual evidence. Color groups can overlap for
multicolored products: adding their counts does not give a catalog total.
Colors come only from explicit whole-word mentions in title, description or
tags, **never from images**; an inferred textual color is not a guarantee
about an unseen product variant. Facets describe available options; use a
filtered query, not summed facets, for combined category/color/budget counts.

`category` accepts real category IDs and the groups `clothing`, `dresses`,
`shirts`, `shoes`, `bags`, `sunglasses`, `jewellery`, including Italian aliases.
In broad shopping conversation, “vestiti” can mean `clothing`: the prompt
asks the agent to state that interpretation briefly. A genuinely ambiguous
request for a specific dress subtype needs a targeted clarification.
`search_products` remains compatible for short lists, not authoritative
totals; `get_product` retrieves an explicit public product ID.

The Italian instructions favor concise, natural replies. Follow-ups such as
“Solo rosse: quante ne avete?” retain relevant category, budget and availability
from earlier turns, replacing only the color. An explicit new search drops
irrelevant old constraints. Prices, stock and counts must still be refreshed
through authorized tools, not inferred from earlier answers or page length.
Inline embeds the service procedure; Skills loads `shop-catalog` and uses the
same business HTTP; the A2A router passes the relevant filters to
`catalog_agent`, which owns the Catalog tools.

## History, instructions and actions

Application-owned router/specialist prompts, optional laboratory blocks,
profile notices and compact-history headings are in Italian. Technical roles,
profile IDs, tool names, JSON keys and block tags are intentionally unchanged.
Native provider/framework-generated boilerplate may remain in English:
this is not a claim to have translated framework internals.

`full` retains the text and chronological order of supplied user/assistant
turns. `compact` normalizes whitespace, JSON-quotes text and refers repeated
assistant text to its earlier identical entry under an Italian heading that
marks history as quoted data, not instructions. Both append the current user
message and retain explicit user corrections; neither performs semantic
summarization or truncation here. Compact packing is **not byte-lossless** and
does not promise a particular token reduction or change model context-window
limits.

There is no separate persistent catalog-filter state in this prompt layer.
The API's `EvidenceStore.HistoryBefore` preserves earlier server-stored messages
including `ProductIds` and `Sources`; the runtime freezes copies of these arrays.
Normal API history therefore retains safe product IDs and source labels, not
images. The compact helper appends these metadata fields when present, whereas
`full` passes only each message's text. Prior tool arguments/results are not
automatically replayed as structured history. Relevant filters explicitly
present in conversation text remain available to router and A2A specialists;
a filter that existed only in a previous tool result is not persistent filter
state. A product ID absent from the answer text also does not enter the
`full` model history merely because it appears in `ProductIds`.
Query again or clarify instead of inventing missing context.

The runtime passes the original request history and verified product
provenance to the configured model context. Product facts are still retrieved
again through `get_product` before they are used in a response.

`bad` is an underspecified control; `good` uses a generic structured procedure. `gpt5` and `gpt6` are explicitly labeled **unoptimized controls**, not model-specific best-practice claims. There is no fabricated GPT-6 parameter or capability compatibility.
All profiles, including `bad`, retain mandatory tool grounding, textual-color
limits, product-versus-stock/page distinctions, trusted customer identity,
server consent, eligibility checks and honest error handling.

The chat prompt laboratory adds five independent `RunConfiguration.PromptBlocks`
switches: `Checklist`, `OutputContract`, `Examples`, `Redundancy`, and
`ConflictingStyle`. All default to false, preserving the **current localized
profile** without extra block text or whitespace. Deserializing an older
configuration also leaves all five disabled; it does not pin a previous
version of the English prompt. Already stored model captures are not rewritten.
Explicit null is invalid. The immutable selection is carried with the frozen run and remote
A2A invocation, not read from mutable UI state during execution.

[PromptLaboratory](PromptLaboratory.cs) holds the block catalog and text.
`AgentPrompts.Instructions` appends enabled blocks in a stable order.
The preview uses that same generator for every active role; no model, skill
provider, domain tool or store is needed to generate it. The returned text is
exactly the configured `ChatOptions.Instructions`, not the complete inference
request: native skill-provider context, tools, history and subsequent tool
results remain runtime additions captured by the existing Inspector.

The substantial redundant block adds ceremony, not domain rules. The conflicting
block contradicts response-style requirements only. Neither can remove the
mandatory grounding, trusted customer/consent or server-side domain checks.
The examples use abstract placeholders, not expected scenario answers.
Selections apply to the router in Inline/Skills and each invoked A2A role;
they do not rewrite service skill packages. The selected prompt blocks are
included in the next LIVE turn and recorded with that run.

Prompt self-tests compare previews and captured instructions against the
current composer, check Italian safety across profiles and block selections,
and validate dynamic UTF-16 character counts rather than frozen historical
lengths. Native Skills context is allowed after the exact configured prefix.
History cases cover explicit filter corrections and compacted repetitions;
catalog-tool checks retain stable argument names with Italian descriptions.
These checks require execution after integration; localization alone is not
evidence of a passing backend suite or improved LIVE model quality.

Product data returned by catalog tools consists of `ProductFact`, including
the `products` array in query responses; counts and facets return their
separate metadata. Tools never expose `Product`/images/thumbnail/base64.
Customer scope is server-bound. `ShopServiceClient` sets
`X-Observatory-Customer-Id` from trusted backend context, and
`X-Observatory-Confirm-Action=true` only from validated
`RunConfiguration.ConfirmAction`. Neither header is a model tool argument
or a UI-supplied service header. A2A instead carries this trusted context in
its invocation metadata. The draft operation rechecks authoritative eligibility;
a chat message cannot grant authorization or change the customer. Drafts are
synthetic; no payment/refund API is called.

## Configuring the optional Azure provider

LIVE is the only supported inference mode. `Demo:AllowLive` defaults to
**false**, matching the API gate. Legacy root `AllowLive` is accepted only
when `Demo:AllowLive` is absent; an explicit `Demo:AllowLive=false` always wins.

The provider is `AzureOpenAIClient.GetChatClient(deployment).AsIChatClient()` using Azure OpenAI Chat Completions. Use configuration/user secrets/environment variables, never committed credentials:

| Setting | Requirement |
|---|---|
| `Demo:AllowLive` | Explicit `true` before live execution on the API and any service doing remote inference; Aspire propagates the backend setting, default `false` |
| `AzureOpenAI:Endpoint` | HTTPS Azure OpenAI endpoint |
| `AzureOpenAI:ApiKey` | Optional; omission uses noninteractive `DefaultAzureCredential`/Entra |
| `Models:{id}:Deployment` | Explicit operator-provided deployment |
| `Models:{id}:Capabilities:FunctionCalling` | Explicit `true` **after** verifying the deployment |
| `Models:{id}:Capabilities:MaxOutputTokens` | Explicit `true` **after** verifying the deployment |
| `Models:{id}:InputPerMillion` / `OutputPerMillion` | Nonnegative configured USD rates; no built-in/guessed prices |
| `Models:{id}:CachedInputPerMillion` | Required for a cost estimate if cached input is reported |
| `Models:{id}:CacheWriteSurchargePerMillion` | Optional; a nonzero rate without a verified usage counter makes cost unpriced |
| `Models:{id}:Currency`, `SourceUrl`, `VerifiedAt`, `Version` | Operator-supplied provenance; currency USD, safe HTTPS source URL, date `yyyy-MM-dd` |
| Per-run `Mode=live`, `ApprovedBudgetUsd>0` | Explicit request authorization |

Pricing accepts the API's flat shape first, with per-field fallback to `Models:{id}:Pricing:{field}` only when the flat field is absent. An explicit empty flat rate remains unconfigured, rather than silently using an older nested rate. Invalid/negative rates and malformed provenance fail explicitly. The API can impose a stricter approval ceiling via `Demo:MaxApprovedBudgetUsd`; the runtime enforces the positive approved amount passed with the run.

Registered **configuration aliases**, not availability claims: `gpt5`,
`gpt6-astra`, `gpt6-sol`, `gpt6-luna`. `AgentModels` overrides apply only to
`router` in Inline/Skills, and may also select `catalog`, `orders`, `returns`
in A2A. Every selected profile must pass validation. Only logical
`ChatOptions.Tools`, `MaxOutputTokens` and `AllowMultipleToolCalls` settings
are supplied. No temperature/reasoning/verbosity compatibility is inferred.

`MaxModelCalls` covers the router's actual calls and, in A2A, imported remote
calls. A2A delegations execute serially and receive the remaining allowance.
There are no additional local specialist calls in Inline/Skills. The USD
budget is checked **between calls using reported usage**; this is **not a
guaranteed pre-call hard billing cap**, because the next response's usage is
unknown. Unknown/unpriceable LIVE usage blocks subsequent calls. Configured
cache-write surcharges without a verified counter make cost unpriced rather
than silently undercounting. Do not enable LIVE when a strict financial
ceiling is required without an external provider/gateway cap.

There is no fallback for missing deployments, unverified capabilities,
invalid provider configuration or A2A failures. Offline tests use injected
runtime fixtures and do not exercise Azure inference.

## Observability

- `Observatory.ServiceDefaults` owns Aspire logs/traces/metrics and the shared
  `.UseObservatoryTelemetry()` overloads. These delegate to official
  Microsoft.Extensions.AI / Agent Framework `.UseOpenTelemetry()`; they do not
  manufacture spans or metrics. It registers both sources **and meters**
  `Experimental.Microsoft.Extensions.AI` and `Experimental.Microsoft.Agents.AI`.
  Sensitive native telemetry is disabled. See [ServiceDefaults](../Observatory.ServiceDefaults/README.md).
- `Evidence/` contains application Inspector capture, sanitization and run
  accounting. These records are durable application data, **not a tracing
  backend**. They remain usable without an OTLP endpoint or trace sampling.
- Exactly one custom `model.completed` ledger record is emitted per actual logical client request, beneath the function loop. Agent/OTel spans are not used to create a second ledger.
- `ModelCallRecord` contains safe logical request/response, actual elapsed time, response ID and trace/span IDs. LIVE ledger usage comes from the provider response's usage object, read before SDK numeric defaults can erase absent-versus-zero distinctions; unreported counts remain null. Native SDK metrics independently use their standard usage representation.
- Missing provider usage or pricing remains unavailable; no token count or cost is fabricated.
- Capture is **logical**, not raw HTTP: `CaptureKind=logical`, `Attempts=[]`. A narrow SDK pipeline policy reads only buffered response usage for billing evidence; it does not retain HTTP bodies or headers. SDK provider retries are disabled; there are no claimed wire-attempt records.
- A2A message parts/results contain domain output only. Run metadata carries
  trusted invocation context, **not telemetry or transport credentials**.
  After each exchange, the client downloads the called role's protected
  evidence from that service origin (the compatibility route is `/telemetry`),
  deduplicates event IDs and preserves typed `ModelCallRecord` data.
  Missing/incomplete evidence fails the run
  rather than silently producing an incomplete total.
- `answer.delta` currently delivers a **buffered final answer**, not provider token streaming.

## Pinned direct package versions

| Package | Version |
|---|---|
| Microsoft.Agents.AI | 1.16.0 |
| Microsoft.Extensions.AI | 10.8.0 |
| Microsoft.Extensions.AI.OpenAI | 10.8.3 |
| Azure.AI.OpenAI | 2.9.0-beta.1 |
| Azure.Identity | 1.21.0 |
| OpenAI | 2.12.0 |
| A2A | 0.3.4-preview |

Agent Framework 1.16.0 is required here for the native skill APIs. The older cached `Microsoft.Agents.AI.A2A`/hosting adapters are not used; the direct official A2A SDK is used identically on both sides. This is the SDK's **A2A 0.3 protocol**, not the later A2A SDK v1 API from newer documentation.

`ToolTransport=direct` means ordinary framework function invocation instead
of MCP. It does **not** mean in-process shop data: Inline/Skills business
HTTP and A2A's remote delegation are real. **MCP is unsupported** and produces
an explicit `unsupported_transport` error.

See [AgentHost README](..\Observatory.AgentHost\README.md) for role-specific
endpoints and executable verification commands. No prior suite counts or
four-agent-per-mode results should be presented as validation of this new
architecture; verify actual router-only versus remote-agent behavior.
