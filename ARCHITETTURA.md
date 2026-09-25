# Architettura delle tre demo

Tre percorsi distinti: **Inline**, **Agent Skills** e **A2A**. Tutti usano
Catalog, Orders e Returns come servizi separati. Sono tre istanze dello
stesso progetto `Observatory.AgentHost`, configurate con
`Agents:Role=catalog|orders|returns`, non tre copie di codice.

Gli schemi mostrano dipendenze e percorsi possibili, non una run gia eseguita:
il router usa solo i tool o gli specialisti necessari alla richiesta.

## Differenze a colpo d'occhio

| Aspetto | Inline | Agent Skills | A2A |
| --- | --- | --- | --- |
| Agenti AI | Solo router nell'API | Solo router nell'API | Router nell'API; uno specialista nel servizio di ciascun ruolo |
| Accesso del router al dominio | Tool ordinari -> HTTP business | Gli stessi tool ordinari -> HTTP business | Tool di delega -> SDK A2A JSON-RPC su HTTP |
| Procedure | Inline nel prompt del router | Tre pacchetti Markdown caricati on demand dal provider nativo del router | Prompt inline del router e degli specialisti |
| Modello/tool loop specialistico | Assente | Assente | Proprio `IChatClient` e ciclo tool per ogni specialista invocato |
| Skill native | Assenti | `shop-catalog`, `shop-orders`, `shop-returns` | Assenti; `AgentCard.Skills` e solo metadata di capacita |
| `capabilities.agentNames` | `["router"]` | `["router"]` | `["router","catalog","orders","returns"]` |
| Risposta finale | Router | Router | Router |

Skills organizza le istruzioni, A2A realizza la delega tra veri agenti.
Non c'e delega `AsAIFunction` a specialisti in-process in Inline o Skills.
Il confronto con A2A cambia anche gli agenti e le possibili chiamate al
modello: non misura il solo overhead di rete a parita di inferenze.

### Come leggere gli schemi

- I riquadri `demo-*` e `*-service` sono confini di processo o container.
- Ogni agente disegnato e un `ChatClientAgent` di Microsoft Agent Framework.
  Una API business o un documento skill **non** e un agente AI.
- Le chiamate d'inferenza, omesse nei grafi, usano Azure OpenAI LIVE.
  Ogni invio richiede configurazione, deployment, capacita, prezzi e consenso
  di spesa; l'avvio e la scelta del grafo non invocano modelli.
- Gli archi HTTP indicano rete reale. Le frecce tratteggiate distinguono
  metadata, istruzioni ed evidenze dal percorso principale.
- I file SQLite sono separati per demo e conservano evidenze. I servizi
  condividono fixture Core e snapshot frozen, **non database di negozio
  indipendenti**; soltanto Orders scrive bozze nello stato persistente.

## Inline: un router, procedure nel prompt, tool HTTP

```mermaid
flowchart TB
    UI["Browser - UI React"] <-->|"HTTP JSON e SSE"| WEB["web - proxy Vite o Nginx"]

    subgraph API["demo-inline - Observatory.Api"]
        ENTRY["API HTTP + RunCoordinator + RunWorker"]
        ROUTER["Unico agente: router<br/>procedure inline nel prompt"]
        TOOLS["ShopFunctions + ShopServiceClient<br/>funzioni ordinarie del framework"]
        META["Catalogo, immagini e provenance<br/>copia metadata/UI; non IShopData"]
        STORE["EvidenceStore"]
        ENTRY -->|"esegue la run"| ROUTER
        ROUTER <-->|"tool calling"| TOOLS
        META -.-> ENTRY
        ENTRY <--> STORE
        ROUTER -.->|"eventi e ledger delle chiamate effettive"| STORE
    end

    subgraph CATALOG["catalog-service - ruolo catalog"]
        CATALOG_HTTP["API Catalog<br/>ricerca, conteggi, faccette, dettagli"]
    end
    subgraph ORDERS["orders-service - ruolo orders"]
        ORDERS_HTTP["API Orders<br/>ordini e bozze"]
    end
    subgraph RETURNS["returns-service - ruolo returns"]
        RETURNS_HTTP["API Returns<br/>policy e valutazioni"]
    end

    WEB <-->|"/api/inline/* diventa /api/*"| ENTRY
    TOOLS <-->|"HTTP /catalog/query, /catalog/facets, /products"| CATALOG_HTTP
    TOOLS <-->|"HTTP /orders e /return-drafts"| ORDERS_HTTP
    TOOLS <-->|"HTTP /policies e /return-assessments"| RETURNS_HTTP
    CATALOG_HTTP -.->|"GET /catalog prima dell'ascolto API"| META
    FIXTURES["Core + snapshot frozen condiviso<br/>.appdata/catalog"] -.-> CATALOG_HTTP
    FIXTURES -.-> ORDERS_HTTP
    FIXTURES -.-> RETURNS_HTTP
    ORDERS_HTTP -->|"solo Orders scrive"| DRAFTS[("Bozze sintetiche persistenti<br/>.appdata/domain")]
    STORE <--> DB[("SQLite della demo inline<br/>conversazioni, run, eventi e ledger")]

    style API fill:#f0fdfa,stroke:#0f766e,color:#134e4a
```

1. L'API registra la richiesta e il worker esegue il router.
2. Il router sceglie tool ordinari come `get_order` o `assess_return`.
   Il framework invoca funzioni C# che effettuano HTTP al servizio competente.
3. Il servizio applica le validazioni di dominio, senza inferenza
   specialistica. Il router usa i risultati per comporre la risposta.

I servizi espongono anche A2A e il proprio pacchetto skill, ma questo percorso
non li usa. Non ci sono agenti Catalog/Orders/Returns nel processo API.

## Agent Skills: lo stesso router HTTP, istruzioni progressive

```mermaid
flowchart TB
    UI["Browser - UI React"] <-->|"HTTP JSON e SSE"| WEB["web - proxy Vite o Nginx"]

    subgraph API["demo-skills - Observatory.Api"]
        ENTRY["API HTTP + RunCoordinator + RunWorker"]
        ROUTER["Unico agente: router"]
        PROVIDER["AgentSkillsProvider nativo<br/>discovery e caricamento progressivo"]
        FILES["Copie bundled fidate e versionate<br/>shop-catalog, shop-orders, shop-returns"]
        TOOLS["ShopFunctions + ShopServiceClient<br/>stessi tool business di Inline"]
        META["Catalogo, immagini e provenance<br/>copia metadata/UI; non IShopData"]
        STORE["EvidenceStore"]
        ENTRY --> ROUTER
        ROUTER <-->|"load_skill / read_skill_resource"| PROVIDER
        FILES -.->|"lettura Markdown locale"| PROVIDER
        ROUTER <-->|"tool calling, non delega"| TOOLS
        META -.-> ENTRY
        ENTRY <--> STORE
        ROUTER -.->|"eventi, skill.loaded e ledger"| STORE
    end

    subgraph CATALOG["catalog-service - ruolo catalog"]
        CATALOG_HTTP["API Catalog + skill shop-catalog"]
    end
    subgraph ORDERS["orders-service - ruolo orders"]
        ORDERS_HTTP["API Orders + skill shop-orders"]
    end
    subgraph RETURNS["returns-service - ruolo returns"]
        RETURNS_HTTP["API Returns + skill shop-returns<br/>risorsa decision-checklist.md"]
    end

    WEB <-->|"/api/skills/* diventa /api/*"| ENTRY
    TOOLS <-->|"HTTP /catalog/query, /catalog/facets, /products"| CATALOG_HTTP
    TOOLS <-->|"HTTP /orders e /return-drafts"| ORDERS_HTTP
    TOOLS <-->|"HTTP /policies e /return-assessments"| RETURNS_HTTP
    CATALOG_HTTP -.->|"GET /catalog prima dell'ascolto API"| META
    FIXTURES["Core + snapshot frozen condiviso<br/>.appdata/catalog"] -.-> CATALOG_HTTP
    FIXTURES -.-> ORDERS_HTTP
    FIXTURES -.-> RETURNS_HTTP
    ORDERS_HTTP -->|"solo Orders scrive"| DRAFTS[("Bozze sintetiche persistenti<br/>.appdata/domain")]
    STORE <--> DB[("SQLite della demo skills<br/>conversazioni, run, eventi e ledger")]

    style API fill:#eff6ff,stroke:#2563eb,color:#1e3a8a
    style PROVIDER fill:#dbeafe,stroke:#2563eb,color:#1e3a8a
```

1. Il router riceve istruzioni di sicurezza e discovery delle skill.
2. `load_skill` carica il relativo `SKILL.md`; `read_skill_resource` puo
   aggiungere, per Returns, `references/decision-checklist.md`.
3. Il router invoca direttamente gli stessi tool HTTP di Inline. Il
   caricamento di una skill non delega a un altro agente.

Il provider legge copie **bundled fidate** degli stessi tre pacchetti
pubblicati dai servizi via `/skills/shop-{role}/SKILL.md`. Non scarica
Markdown dalla rete durante la run e non esegue script. Le copie sono
distribuite e versionate insieme al codice; non esiste una skill `shop-router`.
La checklist aggiuntiva appartiene soltanto al pacchetto Returns.

Le skill contengono procedure, non la fonte autorevole di prezzi, ordini
o policy. I fatti arrivano dalle API business. Il caricamento avviene su
richiesta del router, non eseguendo scenari o modelli allo startup.

## A2A: router e tre servizi con veri agenti remoti

```mermaid
flowchart TB
    UI["Browser - UI React"] <-->|"HTTP JSON e SSE"| WEB["web - proxy Vite o Nginx"]

    subgraph API["demo-a2a - Observatory.Api"]
        ENTRY["API HTTP + RunCoordinator + RunWorker"]
        ROUTER["Router<br/>prompt inline e proprio IChatClient"]
        CLIENT["Tool di delega<br/>A2ATransport + SDK A2A"]
        META["Catalogo, immagini e provenance<br/>copia metadata/UI; non IShopData"]
        STORE["EvidenceStore"]
        ENTRY --> ROUTER
        ROUTER <-->|"catalog_agent / orders_agent / returns_agent"| CLIENT
        META -.-> ENTRY
        ENTRY <--> STORE
        ROUTER -.->|"ledger locale"| STORE
        CLIENT -.->|"import degli eventi e ledger remoti"| STORE
    end

    subgraph CATALOG["catalog-service - ruolo catalog"]
        CATALOG_SERVER["/a2a/catalog + agent card"]
        CATALOG_AGENT["Agente Catalog<br/>proprio modello e ciclo tool"]
        CATALOG_TOOLS["Tool locali -> Core / IShopData"]
        CATALOG_HTTP["GET /catalog per metadata/UI"]
        CATALOG_TELEMETRY["RemoteTelemetryStore in memoria"]
        CATALOG_SERVER <--> CATALOG_AGENT
        CATALOG_AGENT --> CATALOG_TOOLS
        CATALOG_AGENT -.-> CATALOG_TELEMETRY
    end
    subgraph ORDERS["orders-service - ruolo orders"]
        ORDERS_SERVER["/a2a/orders + agent card"]
        ORDERS_AGENT["Agente Orders<br/>proprio modello e ciclo tool"]
        ORDERS_TOOLS["Tool locali -> Core / IShopData"]
        ORDERS_TELEMETRY["RemoteTelemetryStore in memoria"]
        ORDERS_SERVER <--> ORDERS_AGENT
        ORDERS_AGENT --> ORDERS_TOOLS
        ORDERS_AGENT -.-> ORDERS_TELEMETRY
    end
    subgraph RETURNS["returns-service - ruolo returns"]
        RETURNS_SERVER["/a2a/returns + agent card"]
        RETURNS_AGENT["Agente Returns<br/>proprio modello e ciclo tool"]
        RETURNS_TOOLS["Tool locali -> Core / IShopData"]
        RETURNS_TELEMETRY["RemoteTelemetryStore in memoria"]
        RETURNS_SERVER <--> RETURNS_AGENT
        RETURNS_AGENT --> RETURNS_TOOLS
        RETURNS_AGENT -.-> RETURNS_TELEMETRY
    end

    WEB <-->|"/api/a2a/* diventa /api/*"| ENTRY
    CLIENT <-->|"HTTP discovery + JSON-RPC message/send"| CATALOG_SERVER
    CLIENT <-->|"HTTP discovery + JSON-RPC message/send"| ORDERS_SERVER
    CLIENT <-->|"HTTP discovery + JSON-RPC message/send"| RETURNS_SERVER
    CATALOG_TELEMETRY -.->|"GET /telemetry separato"| CLIENT
    ORDERS_TELEMETRY -.->|"GET /telemetry separato"| CLIENT
    RETURNS_TELEMETRY -.->|"GET /telemetry separato"| CLIENT
    CATALOG_HTTP -.->|"GET /catalog prima dell'ascolto API"| META
    FIXTURES["Core + snapshot frozen condiviso<br/>.appdata/catalog"] -.-> CATALOG_TOOLS
    FIXTURES -.-> CATALOG_HTTP
    FIXTURES -.-> ORDERS_TOOLS
    FIXTURES -.-> RETURNS_TOOLS
    ORDERS_TOOLS -->|"solo Orders scrive"| DRAFTS[("Bozze sintetiche persistenti<br/>.appdata/domain")]
    STORE <--> DB[("SQLite della demo a2a<br/>conversazioni, run, eventi e ledger")]

    style API fill:#faf5ff,stroke:#9333ea,color:#581c87
    style CATALOG fill:#f5f3ff,stroke:#7c3aed,color:#4c1d95
    style ORDERS fill:#f5f3ff,stroke:#7c3aed,color:#4c1d95
    style RETURNS fill:#f5f3ff,stroke:#7c3aed,color:#4c1d95
```

1. Il tool di delega usa l'origine configurata per il ruolo e verifica
   `GET /a2a/{role}/.well-known/agent-card.json`.
2. L'SDK ufficiale invia `message/send` JSON-RPC a `/a2a/{role}` di quel
   servizio. Solo lo specialista configurato puo essere eseguito.
3. Lo specialista ha un proprio client di inferenza, prompt inline e ciclo
   tool. Usa le stesse operazioni Core delle sue API business, localmente
   al servizio. Non carica `AgentSkillsProvider` o Markdown nativo.
4. Il risultato di dominio torna al router. Gli eventi remoti sono raccolti
   separatamente da `/telemetry/{runId}?invocationId=...` sull'origine del
   ruolo e poi persistiti dall'API, mai inseriti nel messaggio al modello.

I tre specialisti sono quindi **tre processi/container distinti**, con un
solo progetto e un'unica immagine AgentHost riutilizzata. Non e una REST API
personalizzata ribattezzata A2A. Le `Skills` presenti nella Agent Card sono
metadata di capacita del protocollo, non il provider nativo della demo Skills.
Le API business e i file skill restano esposti dai servizi, anche se il
percorso A2A usa i loro agenti e non carica quei file.

## Contratti e confini di fiducia

Le origini sono `Agents:Endpoints:catalog`, `Agents:Endpoints:orders` e
`Agents:Endpoints:returns`. Non includono percorsi `/a2a` o `/api`.

| Servizio | Endpoint business | Tool AI corrispondente |
| --- | --- | --- |
| Catalog | `GET /catalog` | Nessuno: snapshot completo soltanto per metadata/UI |
| Catalog | `GET /products?query=&maxPrice=&take=` | `search_products` |
| Catalog | `GET /catalog/query?query=&category=&color=&maxPrice=&inStockOnly=&take=` | `query_catalog` |
| Catalog | `GET /catalog/facets` | `get_catalog_facets` |
| Catalog | `GET /products/{productId:int}` | `get_product` |
| Orders | `GET /orders/{orderId}` | `get_order` |
| Orders | `POST /return-drafts` con `{orderId,reason}` | `create_return_draft` |
| Returns | `GET /policies` | `get_policies` |
| Returns | `POST /return-assessments` con `{orderId,reason}` | `assess_return` |

Ogni servizio mappa soltanto le proprie API business, il proprio
`/a2a/{role}` con agent card e `/skills/shop-{role}/SKILL.md`.
Returns aggiunge `/skills/shop-returns/references/decision-checklist.md`.
Inline/Skills espongono al router tutti i tool business; A2A espone al router
solo i tre tool di delega e a ogni specialista solo i tool del proprio ruolo.

- `X-Observatory-Customer-Id` viene impostato dal backend sull'identita
  fidata, non da un argomento generato dal modello o da un header UI.
- `X-Observatory-Confirm-Action=true` viene impostato dal backend solo dopo
  consenso esplicito validato. Orders rivaluta l'ammissibilita prima di
  creare una bozza; testo del modello e semplice richiesta HTTP non bastano.
- `X-Observatory-A2A-Key` autentica **tutti** gli endpoint backend non-health
  quando `Agents:AllowRemote=true`: business, skill, discovery, A2A,
  telemetria e indice. Solo `GET /health` e `GET /alive` sono esenti.
- A2A trasporta customer/consenso e configurazione nella metadata fidata
  della richiesta backend, non nel testo di dominio affidato al modello.
- Il browser usa soltanto le API delle demo attraverso `web`; non riceve
  il segreto e non chiama direttamente i servizi.

Le immagini sono URL pubblici per la UI: i tool AI restituiscono
`ProductFact` e aggregati senza immagini. `query_catalog` conta tutti i
modelli e pezzi corrispondenti ai filtri prima di applicare `take` agli
esempi. `get_catalog_facets` espone categorie e colori testuali reali.
I colori derivano da titolo, descrizione e tag, non da analisi delle immagini;
un prodotto multicolore puo appartenere a piu faccette.
Le bozze sono sintetiche, non eseguono
rimborsi o pagamenti.

`ToolTransport=direct` distingue funzioni del framework da MCP, **non**
dominio in-process. Le chiamate business HTTP di Inline/Skills sono reali;
in A2A sono reali discovery, delega e raccolta telemetria HTTP. MCP non e
implementato e non va disegnato come un quarto percorso disponibile.

## Avvio, persistenza e osservabilita comuni

Un solo Aspire AppHost avvia `web`, `demo-inline`, `demo-skills`, `demo-a2a`,
`catalog-service`, `orders-service`, `returns-service`. Tutte le API ricevono
le tre origini dinamiche e attendono la salute di tutti i servizi.
Il proxy conserva `/api/inline`, `/api/skills`, `/api/a2a`.

A processi, l'accesso ai servizi e loopback-only. Nei container, Aspire
propaga il segreto generato e `Agents:AllowRemote=true` ai soli backend.
Il publish dell'immagine AgentHost precede quello dell'immagine API per
evitare scritture concorrenti negli output condivisi; ogni immagine e
riutilizzata da tre istanze.

### Inizializzazione di base, non esecuzione degli scenari

1. I servizi inizializzano `ShopData` e le fixture Core prima dell'ascolto.
   `.appdata\catalog\products.snapshot.json` viene copiato atomicamente
   dallo snapshot bundled solo se manca. Una copia esistente e validata e
   riutilizzata, senza alterare acquisizione/hash o cancellare bozze.
2. Le API non inizializzano un dominio locale: prima dell'ascolto caricano
   catalogo, immagini e provenance tramite `GET /catalog` di Catalog.
   Questa copia serve a UI e metadata delle run, non ai tool del modello.
3. Gli scenari sono definizioni locali. Non si crea alcuna conversazione
   o run, non si invocano modelli e non si scaricano dati da Internet.
   Il fetch interno HTTP verso Catalog non e un seed o uno scenario.

I servizi condividono lo snapshot e le fixture deterministiche della
libreria Core. Solo Orders scrive le bozze in `.appdata\domain`; non ci
sono database di dominio separati per servizio. Nei container questi
percorsi sono `/state/catalog` e `/state/domain`. Uno snapshot non valido
interrompe lo startup, senza fallback silenzioso.

Ogni API conserva solo il proprio file `.appdata\{tecnologia}\observatory.sqlite`,
montato come `/state/{tecnologia}/observatory.sqlite` nei container.
La separazione di SQLite riguarda conversazioni, run, timeline e ledger,
non il catalogo o gli ordini del negozio.

Ci sono due destinazioni distinte delle evidenze:

- `EvidenceStore`/SQLite della demo: eventi, ledger per-call, UI, SSE, replay
  ed export, inclusi gli eventi remoti importati in A2A.
- OpenTelemetry/OTLP: span, log e metriche nella dashboard Aspire. Non
  sostituisce SQLite e non va sommato al ledger come ulteriore fatturazione.

SSE trasmette eventi applicativi e una risposta bufferizzata, non token
streaming del provider. Token e costo sono mostrati solo quando disponibili
dall'usage del provider e dalle tariffe configurate. Questi schemi non
costituiscono un report di test o benchmark gia completati.

## Riferimenti nel codice

- [Orchestrazione Aspire](src/Observatory.AppHost/AppHost.cs)
- [Router, tool ordinari, specialisti A2A e provider Skills](src/Observatory.Agents/ObservatoryAgentRuntime.cs)
- [Istruzioni e profili](src/Observatory.Agents/AgentPrompts.cs)
- [Pacchetti skill dei servizi](src/Observatory.Agents/Skills)
- [Client HTTP business](src/Observatory.Agents/ShopServiceClient.cs)
- [Client A2A e importazione telemetria](src/Observatory.Agents/A2ATransport.cs)
- [Host configurato per ruolo](src/Observatory.AgentHost/AgentHostApplication.cs)
- [API business e file skill per ruolo](src/Observatory.AgentHost/BusinessEndpoints.cs)
- [Client di inferenza e cattura logica](src/Observatory.Agents/ModelProviderFactory.cs)
- [Tool e validazioni](src/Observatory.Agents/ShopFunctions.cs)
- [Fixture Core e inizializzazione snapshot](src/Observatory.Core/ShopData.cs)
- [Startup API](src/Observatory.Api/Program.cs)
- [Caricamento metadata da Catalog](src/Observatory.Api/RemoteShopCatalog.cs)
- [Coordinamento e worker](src/Observatory.Api/RunProcessing.cs)
- [Persistenza delle evidenze](src/Observatory.Api/EvidenceStore.cs)
- [OpenTelemetry condiviso](src/Observatory.ServiceDefaults/Extensions.cs)
