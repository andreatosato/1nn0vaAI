# AI Observatory - tre demo con Aspire

Laboratorio per la sessione **Osservare l'AI per migliorare la qualita delle
risposte e abbassare il costo dei modelli**.

La specifica iniziale e in [PROMPT-DEMO-OSSERVABILITA-AI.md](PROMPT-DEMO-OSSERVABILITA-AI.md);
contiene anche obiettivi non implementati, come MCP. Questa guida e gli schemi
descrivono il perimetro corrente, con tre servizi di dominio separati.
Gli schemi separati di **Inline, Agent Skills e A2A**, con persistenza e
osservabilita, sono in [Architettura delle demo](ARCHITETTURA.md).
La proposta e la scaletta dello speaker per una sessione di 60 minuti sono in
[presentazione/SCALLETTA-60-MINUTI.md](presentazione/SCALLETTA-60-MINUTI.md).

## Cosa esegue

Un Aspire AppHost avvia React/Vite e **dodici processi .NET separati**. Ogni
agente ha la propria API, il proprio modello, il proprio prompt e i propri
tool, come in una vera architettura agentica. Il codice e diviso per ruolo:

| Cartella | Progetti | Ruolo | Modello |
| --- | --- | --- | --- |
| [src/Shop](src/Shop) | `Observatory.Shop.Catalog`, `.Orders`, `.Returns` | API di business, sistema di record | No |
| [src/Agents](src/Agents) | `Observatory.Agent.Catalog`, `.Orders`, `.Returns` | Agenti specialisti A2A: ognuno ha modello, procedura e tool HTTP nella propria cartella `Tools` | Si |
| [src/Skills](src/Skills) | `Observatory.Skill.Catalog`, `.Orders`, `.Returns` | Skill site HTTP: pubblicano gli specialisti come Agent Skills | No |
| [src/Routers](src/Routers) | `Observatory.Router.Inline`, `.Skills`, `.A2A` | Un router per demo; la UI parla solo con i router | Si |
| [src/Shared](src/Shared) | `Core`, `ServiceDefaults`, `AgentRuntime`, `RouterHost`, `SpecialistHost` | Infrastruttura comune che la demo non deve mostrare | - |

| Demo | Agenti con modello | Accesso al dominio | Istruzioni |
| --- | --- | --- | --- |
| Inline | `router-inline` | Tool HTTP verso `shop-catalog`, `shop-orders`, `shop-returns` | Tutte nel prompt del router |
| Skills | `router-skills` | Tool HTTP verso `shop-catalog`, `shop-orders`, `shop-returns` | Tre Agent Skills native (`catalog`, `orders`, `returns`) caricate via HTTP da `skill-*` |
| A2A | `router-a2a` e i tre `agent-*` | Delega A2A agli agenti; ogni agente usa i tool HTTP della propria API | Prompt di ogni agente |

Un agente e **istruzioni + tool** (interfaccia `IAgent`): lo si legge tutto in
un file, per esempio [CatalogAgent.cs](src/Agents/Observatory.Agent.Catalog/CatalogAgent.cs)
o [InlineRouter.cs](src/Routers/Observatory.Router.Inline/InlineRouter.cs).
Modello, cattura delle evidenze, limiti e telemetria sono condivisi in
[AgentRuntime](src/Shared/Observatory.AgentRuntime). I tool HTTP sono gli stessi
in tutte le architetture (nome, descrizione e schema identici): cambia solo
quale agente li usa. Il testo condiviso e duplicato di proposito, per esempio
la procedura del catalogo in Inline e nell'agente Catalog, invece di aggiungere
switch di configurazione.


Le skill non sono agenti ne un protocollo di trasporto. Il provider del router
Skills legge l'indice dei tre **skill site** fidati (`skill-catalog`,
`skill-orders`, `skill-returns`) con la service discovery di Aspire e scarica
`SKILL.md` solo quando il modello invoca `load_skill`. I nomi delle skill sono
`catalog`, `orders`, `returns`; non esistono skill `shop-*` o `shop-router`.
I contenuti `SKILL.md` sono la procedura dello specialista corrispondente,
pubblicata verbatim. Le risorse sono solo Markdown dichiarato nell'indice
(per Returns: `references/decision-checklist.md`); nessuno script viene
scoperto o eseguito. `AgentCard.Skills` di A2A descrive invece capacita del
protocollo, non questi documenti o il loro caricamento.

`GET /api/config` espone `capabilities.agentNames`: `["router"]` per
Inline/Skills e `["router","catalog","orders","returns"]` per A2A.
La scelta dei modelli per agente e la mappa della UI devono rispettare questa
distinzione; il ledger registra solo gli agenti effettivamente invocati.

L'unica modalità di inferenza offerta è **LIVE** tramite Azure OpenAI.
Ogni invio richiede configurazione valida, modello pronto, budget esplicito e
consenso dell'utente. Lo startup non invia richieste ai modelli.

## Prerequisiti

- .NET SDK 10.0.401, oppure patch successiva dello stesso feature band.
- Node.js 24 LTS e npm.
- Aspire CLI / integrazione Aspire in VS Code per la dashboard e il ciclo di avvio.

Il progetto AppHost usa Aspire SDK e integrazione JavaScript 13.4.6.
La CLI puo essere piu recente. Le versioni dei package sono nei progetti;
non occorre installare globalmente altri tool per chiamare Azure.

In questo ambiente il feed `api.nuget.org` ha restituito errori TLS. Il
[NuGet.Config](NuGet.Config) usa il mirror pubblico ufficiale Microsoft
`dotnet-public` e il feed HTTPS NuGet v2, senza disabilitare i certificati.
La configurazione e limitata a questo repository.

## Avvio locale, senza Docker

Da PowerShell nella radice:

```powershell
npm --prefix .\src\Observatory.Web ci
dotnet build .\AiObservatory.slnx
dotnet run --project .\src\Observatory.AppHost --launch-profile http
```

In VS Code e preferibile avviare l'AppHost tramite l'integrazione Aspire.
Nella dashboard apri il collegamento HTTP della risorsa `web`.
Gli endpoint delle risorse sono assegnati dinamicamente; non presumere una
porta fissa per frontend o API.
Il profilo `http` abilita esplicitamente il trasporto non TLS per lo sviluppo
locale. Il profilo `https` resta disponibile con certificati di sviluppo validi.

Risorse nella dashboard:

- `web`: frontend React.
- `shop-catalog`, `shop-orders`, `shop-returns`: API di business.
- `agent-catalog`, `agent-orders`, `agent-returns`: agenti specialisti A2A.
- `skill-catalog`, `skill-orders`, `skill-returns`: skill site senza modello.
- `router-inline`, `router-skills`, `router-a2a`: le tre demo.

I componenti si trovano per nome logico con la **service discovery di Aspire**
(`http://shop-orders`, `http://agent-orders`): nessun URL, ruolo o tecnologia
da configurare. Fuori da Aspire ogni progetto ha una porta fissa nel proprio
`launchSettings.json`: API 5301-5303, agenti 5311-5313, router 5321-5323,
skill site 5331-5333.


La UI usa richieste relative. Il proxy riceve gli indirizzi da Aspire:
`INLINE_API_URL`, `SKILLS_API_URL`, `A2A_API_URL`.
Nessuna chiave Azure viene passata al browser o a variabili `VITE_*`.

Ferma l'app dalla dashboard/editor Aspire oppure con Ctrl+C nel terminale
che ha avviato l'AppHost. Non terminare indiscriminatamente tutti i processi .NET.

### Dati di base pronti allo startup

Non serve lanciare uno script PowerShell o un progetto di caricamento separato.
Le tre API di business inizializzano le fixture di `ShopData` prima di accettare
richieste: catalogo, ordini sintetici e policy. I router non registrano un
`IShopData` locale: prima di ascoltare chiamano **`GET /catalog` del servizio
Catalog**, caricando snapshot, URL immagine e provenance per metadata/UI.
Un errore non viene mascherato da un catalogo locale alternativo.
Le definizioni degli scenari rimangono locali alla libreria Core.

Al primo avvio lo snapshot incluso nel progetto viene validato e copiato in
`.appdata\catalog\products.snapshot.json`. Aspire passa lo stesso percorso alle
tre API di business. La copia e atomica anche
con avvii concorrenti; se lo snapshot esiste viene validato e riutilizzato,
senza sovrascriverlo, modificarne hash/data di acquisizione o cancellare bozze.
Uno snapshot non valido interrompe lo startup con un errore esplicito.

Il caricamento di base e .NET e offline rispetto ai servizi esterni: il fetch
HTTP delle API verso Catalog e interno al laboratorio. Nessun download da DummyJSON,
nessuna chiamata a modelli, nessuna conversazione o run viene creata.
Non vengono eseguiti automaticamente ne scenari ne test, e non serve un
pulsante UI. I log di startup indicano quanti prodotti e policy sono pronti
e l'hash del catalogo.

Catalogo e fixture frozen sono condivisi tramite Core e lo stesso snapshot:
**non ci sono tre database di negozio indipendenti**. Solo Orders scrive le
bozze sintetiche nello stato persistente condiviso `.appdata\domain`. I tre router hanno invece file SQLite separati
per conversazioni, run ed evidenze, non per i dati del negozio.

### Contratti dei servizi

I percorsi sono relativi all'origine del servizio, non a `/api`.

| Servizio | Endpoint business |
| --- | --- |
| Catalog | `GET /catalog`: snapshot completo, **solo metadata/UI**, non tool AI |
| Catalog | `GET /products?query=&maxPrice=&take=`; `GET /products/{productId:int}`: fatti prodotto senza immagini |
| Catalog | `GET /catalog/query?query=&category=&color=&maxPrice=&inStockOnly=&take=`: ricerca combinata, conteggio completo e campione di prodotti |
| Catalog | `GET /catalog/facets`: categorie e colori testuali presenti, con conteggi |
| Orders | `GET /orders/{orderId}`; `POST /return-drafts` con body `{orderId,reason}` |
| Returns | `GET /policies`; `POST /return-assessments` con body `{orderId,reason}` |

Le API di business espongono soltanto le proprie rotte: non servono skill.
I tre skill site espongono `GET /skills`, `GET /skills/{name}/SKILL.md`
e, per Returns, `GET /skills/returns/references/decision-checklist.md`.
Ogni agente specialista espone `/a2a/{ruolo}`, la relativa
`/a2a/{ruolo}/.well-known/agent-card.json` e `POST /prompts/preview` con le
proprie istruzioni: il router A2A compone l'anteprima chiedendola agli agenti.

Il backend imposta `X-Observatory-Customer-Id` dall'identita cliente fidata e
`X-Observatory-Confirm-Action=true` solo con consenso esplicito validato.
Non sono argomenti del modello ne header che la UI inoltra ai servizi.
La conferma nel testo della chat non autorizza una bozza. I servizi accettano
solo host locali (`AllowedHosts`) e non sono pensati per l'esposizione in rete.


## Catalogo pubblico e immagini

La fonte e [DummyJSON Products](https://dummyjson.com/docs/products), un'API
pubblica di **dati di esempio** destinata a test e prototipi, non un negozio.
Lo snapshot incluso nel progetto contiene 38 prodotti di abbigliamento/accessori acquisiti tramite
la proiezione documentata dell'API. La provenienza e registrata insieme a data
di acquisizione e hash.

Sono esclusi recensioni, nomi/email dei recensori e QR code. Le immagini
rimangono URL pubblici per la UI; nessun file immagine viene redistribuito.
Conservare l'attribuzione e verificare i termini della fonte prima di usi
diversi dalla dimostrazione.

`Import-Products.ps1` rimane soltanto uno strumento opzionale di manutenzione
per riacquisire i dati pubblici, non un prerequisito di avvio. Per sostituire
esplicitamente il catalogo persistente di Aspire, ferma prima l'app:

```powershell
.\scripts\Import-Products.ps1 -OutputPath .\.appdata\catalog\products.snapshot.json -Refresh
```

Riavvia poi l'AppHost: i servizi leggeranno il nuovo snapshot e le API ne
caricheranno metadata e prodotti da Catalog. Senza
`-OutputPath` lo script gestisce lo snapshot sorgente in `data`, incluso
nelle build successive, ma non sostituisce un catalogo runtime gia presente.
L'import non sovrascrive uno snapshot esistente senza `-Refresh` e non ripiega
silenziosamente su dati finti se la fonte non e disponibile. Lo startup non
riacquisisce dati dalla rete nemmeno quando la directory runtime e vuota:
usa sempre lo snapshot distribuito con l'applicazione.
Le prove A/B devono usare lo stesso snapshot, non riacquisire il catalogo.
Le immagini possono richiedere connettivita al CDN; la chat usa lo snapshot
frozen tramite i servizi del laboratorio.

`Product` contiene i riferimenti immagine per React; `ProductFact` e il DTO
destinato ai tool AI e ne e privo. La UI associa i product ID della risposta
alle schede del catalogo senza aggiungere immagini al messaggio per il modello.

Gli ordini, le policy e le bozze sono sintetici. USD e una convenzione della
demo per gli importi del catalogo, non una quotazione commerciale reale.

### Pagine condivise e confronti

Per provare il sistema con impostazioni ripetibili, **Confronti guidati**
(`/#/{tech}/examples`) offre tre preset completi: architetture, prompt
BAD/GOOD/prolisso e GPT-5/GPT-6 Sol. La pagina mostra titolo e varianti;
criteri, conversazione e configurazione completa sono raccolti in dettagli
espandibili. **Prepara** configura la chat e compila il primo messaggio, ma non
lo invia. Tutti i parametri, i messaggi e le attese sono in
[ESEMPI-PREIMPOSTATI.md](ESEMPI-PREIMPOSTATI.md).

La pagina globale **Confronto misure** (`/#/confronto`) legge lo storico di
Inline, Agent Skills e A2A e raggruppa i run per lo stesso testo utente
(normalizzando soltanto spazi iniziali/finali e spaziature ripetute). Mostra
tempo end-to-end, primo output, token, stato e costo USD registrato. Non avvia
inferenze; misure o prezzi assenti non vengono presentati come zero.
Per confronti controllati, mantenere le altre impostazioni uguali e ripetere
le misure.

**Dati della demo** (`/#/dati`) è una route condivisa, non una scheda delle
singole demo. Mostra lo stesso catalogo, gli ordini sintetici e le policy
per Inline, Skills e A2A; i pulsanti “Chiedi” preparano la domanda per la
chat della demo da cui si è arrivati. La vecchia route `/#/{tech}/data`
continua a portare alla pagina condivisa.

La pagina offre prodotti ricercabili con descrizione completa, marca, SKU,
tag e immagini; ordini con cliente, prodotto, listino, importo pagato, outlet,
consegna, stato e tracking; policy integrali con priorita e versione.
Ordini e policy arrivano dal nuovo `GET /api/demo-data`, che legge i servizi
Orders e Returns via HTTP, senza ricostruire copie locali del dominio.
**Aggiorna dati** e la navigazione non eseguono modelli o azioni.

In ogni tecnologia, **Configurazione** (`/#/{tech}/chat`) contiene modelli,
prompt e anteprima; le altre pagine della demo restano nei relativi tab.

E una vista didattica in sola lettura dell'intero dataset sintetico:
gli ordini di altri clienti sono segnalati e filtrabili, ma gli agenti
continuano ad accedere solo agli ordini autorizzati del cliente della chat.
I pulsanti **Chiedi del prodotto / dell'ordine** aprono lo stesso bot
flottante e preparano esclusivamente testo, senza inviare ne concedere
consensi. Impostazioni e conversazione restano intatte fra le pagine.

### Ricerca e conversazione sul catalogo

Il catalogo non viene inserito integralmente nel prompt. Gli agenti lo
consultano con tool che restituiscono soltanto dati pubblici senza immagini:

- `query_catalog`: combina parole chiave, categoria, colore, prezzo massimo
  **inclusivo** in USD e disponibilita. `totalProducts` conta i modelli
  distinti; `stockUnits` somma i pezzi; `inStockProducts` conta i modelli
  con stock positivo. `take` (1-30, default 5) limita **solo gli esempi**,
  non questi totali; `hasMore` segnala altri risultati.
- `get_catalog_facets`: scopre categorie e colori realmente presenti,
  invece di indovinare i filtri.
- `search_products` e `get_product`: ricerca testuale semplice e dettagli
  aggiornati di un ID pubblico; non si conta la lista troncata di
  `search_products` per rispondere a domande aggregate.

Categoria e colore accettano termini italiani e inglesi. Nel contesto
generico della demo, "vestiti" indica `clothing`: abbigliamento senza scarpe,
borse o gioielli. Per categorie piu specifiche si usano i gruppi
`dresses`, `shirts`, `shoes`, `bags`, `sunglasses`, `jewellery`, oppure gli
identificatori effettivi restituiti dalle faccette.

Lo snapshot **non ha un attributo colore strutturato**: il filtro cerca
parole esplicite in titolo, descrizione e tag, mai nelle immagini. Un
articolo rosso e nero compare in entrambi i colori: i conteggi delle
faccette colore non sono additivi. Non sono certificate taglie, varianti o
caratteristiche dedotte dalle fotografie.

Sequenza riproducibile sullo snapshot incluso:

1. `Mi dici quanti vestiti rossi hai?` → **1 modello, 62 pezzi**:
   ID 181, Marni Red & Black Suit, 179.99 USD.
2. `E sotto i 100 dollari?` → **0 modelli, 0 pezzi**, conservando categoria e colore.
3. `E le scarpe rosse?` → **1 modello, 7 pezzi**, mantenendo il limite di 100 USD.
4. `Senza limite di prezzo` → **4 modelli, 93 pezzi** di scarpe rosse.
5. `Quanto costa il primo?` → dettaglio dell'ID 189, primo nell'elenco per prezzo.

In Inline e Skills il router usa le API HTTP del servizio Catalog; in A2A
delega al suo agente remoto, che usa gli stessi calcoli autorevoli.
Le risposte includono i filtri applicati, gli ID e la provenienza; un errore
di servizio non viene trasformato in disponibilita zero.

Le richieste conversazionali vengono interpretate dal modello LIVE e possono
attivare tool Catalog; verifica nella traccia quali chiamate sono avvenute.
Per confronti qualitativi servono esecuzioni autorizzate e ripetibili.

## Storia da mostrare sul palco

Il [set di domande per la demo](DOMANDE-DEMO.md) contiene il percorso
multi-turn che coinvolge tutti i domini, domande mirate, risultati attesi
e una checklist per confrontare output, fonti e misure effettive.

Il clock applicativo e fissato al 23 settembre 2026. ORD-1042 riguarda il
prodotto pubblico 83, Blue & Black Check Shirt, consegnato il 5 settembre:

1. Dov'e il mio ordine ORD-1042?
2. Posso restituirlo? Non l'ho usato.
3. Preciso meglio: la camicia era difettosa al primo utilizzo.
4. Consigliami una camicia simile sotto i 40 dollari.
5. Costava 29.99: il rimborso sara di 29.99?
6. Confermo: prepara la richiesta di reso per il difetto.

L'outlet limita il ripensamento a 14 giorni; il difetto ha un'eccezione a 60.
L'importo pagato e 19.99, distinto dal prezzo di catalogo 29.99.
La bozza richiede anche il consenso esplicito nell'interfaccia; una frase
prodotta dal modello non vale come consenso e non si eseguono pagamenti.

## Laboratorio del prompt nella chat

Nel pannello del prossimo turno puoi "commentare/decommentare" cinque blocchi
senza modificare C# o riavviare la demo:

| Blocco | Cosa permette di mostrare |
| --- | --- |
| Checklist del compito | Obiettivo, identificatori, correzioni e verifica dei fatti |
| Contratto di risposta | Formato e dettaglio proporzionati alla richiesta |
| Esempi di comportamento | Casi astratti, senza fornire risposte delle fixture al modello |
| Prosa ridondante | Un prompt molto piu lungo, ma senza nuove regole utili |
| Stile contraddittorio | Istruzioni incompatibili sulla presentazione, non sulle autorizzazioni |

Tutti i blocchi sono inizialmente spenti: il profilo `bad`, `good`, `gpt5` o
`gpt6` usa le proprie istruzioni di base in italiano. Il profilo e i blocchi sono
due dimensioni separate. **Lungo non significa scorretto**: confronta prima
la base con checklist, contratto ed esempi; aggiungi poi soltanto ridondanza
oppure contraddizioni, mantenendo invariati modello, scenario e history.
Per un confronto controllato usa nuove conversazioni con la stessa sequenza
di messaggi: aggiungere un turno cambia anche il contesto.

L'anteprima proviene da `POST /api/prompts/preview`, che usa lo stesso
compositore del runtime, senza creare conversazioni/run o chiamare modelli/tool.
Mostra le **istruzioni configurate esatte** per ciascun agente attivo e il loro
numero di caratteri, non una stima dei token. Non e il payload completo:
history, messaggio utente, tool e contesto del provider Skills si aggiungono
a runtime. Il caricamento delle skill dipende dalle scelte del modello;
le richieste effettive restano nell'Inspector.

La selezione `configuration.promptBlocks` viene congelata con il turno,
salvata nelle evidenze, esportata e inoltrata agli specialisti A2A.
In Inline/Skills modifica il router; in A2A ogni agente effettivamente invocato.
Non modifica i documenti `SKILL.md` ne il numero di agenti dell'architettura.
Identita cliente, grounding, conferma server ed eligibility non sono interruttori:
restano obbligatori, e i servizi continuano a controllarli.

Le schede **GPT-6, GPT-5.6, GPT-5.4 e GPT-5** nella chat sono consigli con
fonti, non nuovi deployment o preset automaticamente ottimizzati. La scheda
scelta non cambia il modello eseguito. I profili eseguibili restano quelli
elencati nella sezione seguente.

Le conclusioni sulla qualita, sul ragionamento, sulla latenza o sul costo
richiedono valutazioni LIVE controllate, autorizzate e ripetute.
L'anteprima non autorizza alcuna spesa.

### Quando si aggiorna il bot dopo una modifica ai Settings

Le impostazioni riguardano il **prossimo messaggio inviato**. Modello,
profilo del prompt, blocchi opzionali e strategia di history vengono
acquisiti con la richiesta, salvati nella run e usati per creare gli agenti
di quel turno. Non occorre riavviare il progetto o ridistribuire il bot.
Un override di modello per un agente continua a prevalere sul modello
predefinito: cambiarlo non sostituisce implicitamente gli override.

- Una run gia avviata o in coda conserva la propria configurazione.
- La modifica e l'anteprima non avviano inferenze, tool o nuovi turni.
- Conversazione, risposte precedenti ed evidenze rimangono; il nuovo
  messaggio usa la history scelta con il nuovo prompt/modello.
- Il replay mostra la run originale, non la riesegue con i Settings attuali.

Per vedere GPT-5 → GPT-6 o prompt `bad` → `good` nella stessa chat basta
inviare un nuovo messaggio dopo la modifica. Per **misurare la differenza**
senza confondere gli effetti della history, usa invece due nuove
conversazioni con identica sequenza di messaggi e modifica una sola
variabile alla volta.

Le istruzioni applicative di router e specialisti, i cinque blocchi del
laboratorio e i pacchetti skill sono in italiano; gli identificatori di
tool/JSON, i nomi dei prodotti e i dati originali del catalogo non vengono
tradotti. Eventuale testo tecnico generato dal framework non e una
traduzione del prompt applicativo.

## Profili GPT e collegamento ad Azure

I profili richiesti sono `gpt5`, `gpt6-astra`, `gpt6-sol`, `gpt6-luna`.
Il nome del deployment Azure e configurabile e non e necessariamente il nome
del modello. I confronti desiderati sono:

- GPT-5 contro ciascun GPT-6 disponibile.
- GPT-6 Astra contro GPT-6 Luna.
- GPT-6 Sol contro GPT-6 Luna.

Prima dell'esecuzione verificare disponibilita, versione, capacita, regione e tariffa
dei deployment. Un profilo senza deployment non deve essere sostituito
automaticamente con un altro modello.

Le indicazioni di prompting non sono una semplice sostituzione del nome:
la [guida GPT-6](https://developers.openai.com/api/docs/guides/latest-model)
e l'articolo ufficiale
[Rethinking skills and prompts for GPT-6 Astra](https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra)
invitano a rivedere istruzioni ridondanti, descrizioni delle skill troppo
ampie e vincoli ereditati. Sono punti di partenza da misurare, non la prova
che lo stesso prompt sia ottimale per Astra, Sol e Luna.
Anche [Microsoft raccomanda la valutazione per task](https://azure.microsoft.com/en-us/blog/gpt-6-astra-sol-and-luna-for-production-agents-in-microsoft-foundry/),
non il confronto del solo prezzo per token.

Configurazione lato AppHost:

- `Demo:AllowLive`: disabilitato per default; `AllowLive` e un alias legacy,
  considerato solo se il parametro canonico e assente.
- `AzureOpenAI:Endpoint`: endpoint Azure, mai quello dell'API pubblica OpenAI.
- `Models:<profile>:Deployment`, `ModelVersion`, `Region`, `DeploymentType`.
- Tariffe verificate per profilo, fonte URL e data: nessun prezzo di default.
- `Models:<profile>:Capabilities:FunctionCalling` e `MaxOutputTokens`:
  conferme esplicite delle capacita del deployment prima del LIVE.
- Se serve una API key, usare il secret AppHost `Parameters:azure-openai-key`.
  Il servizio riceve il valore come `AzureOpenAI__ApiKey`, non il frontend.

Non salvare chiavi in file tracciati. Il consenso di spesa e distinto dalla
semplice presenza delle credenziali. Un errore del provider viene mostrato come errore.
Questo e un laboratorio locale con identita cliente sintetica e payload
ispezionabili, non un servizio pubblico multiutente: non esporre le API su
Internet e non usare dati personali o credenziali nei messaggi di chat.

### Foundry della demo

Configurazione Azure verificata il **24 settembre 2026** tramite ARM e
l'elenco deployment dell'API del progetto, autenticata con Microsoft Entra ID.
La verifica non ha eseguito inferenze.

| Elemento | Valore |
| --- | --- |
| Sottoscrizione | `MCAPS-1500-andreatosato` (`6a198800-ae12-4e00-9843-ac6b716379b2`) |
| Resource group | `rg-1nn0vaai-demo-swc` |
| Risorsa Foundry | `ai-1nn0vaai-demo-swc-6a198800` |
| Progetto predefinito | `proj-1nn0vaai-demo`, nome visualizzato `1nn0vaAI Observatory` |
| Regione della risorsa e del progetto | `swedencentral` |

La risorsa e un account `Microsoft.CognitiveServices/accounts` di tipo
`AIServices`, con `allowProjectManagement=true`. Il progetto e una risorsa
figlia `Microsoft.CognitiveServices/accounts/projects`, non un workspace o
hub Azure Machine Learning classico. Nel modello Foundry attuale i deployment
dei modelli appartengono alla risorsa padre e sono disponibili al progetto:
non devono essere duplicati dentro il progetto.

Apri il [progetto nel portale Azure](https://portal.azure.com/#@16b3c013-d300-468d-ac64-7eda0820b6d3/resource/subscriptions/6a198800-ae12-4e00-9843-ac6b716379b2/resourceGroups/rg-1nn0vaai-demo-swc/providers/Microsoft.CognitiveServices/accounts/ai-1nn0vaai-demo-swc-6a198800/projects/proj-1nn0vaai-demo/overview)
oppure seleziona `1nn0vaAI Observatory` in [Microsoft Foundry](https://ai.azure.com/).
La [documentazione sui progetti Foundry](https://learn.microsoft.com/azure/foundry/how-to/create-projects)
descrive questa relazione e il ruolo del progetto predefinito.

| Profilo dell'app | Nome del deployment e modello | Versione | Token/minuto | Richieste/minuto |
| --- | --- | --- | --- | --- |
| `gpt5` | `gpt-5` | `2025-08-07` | 50.000 | 500 |
| `gpt6-astra` | `gpt-6-astra` | `2026-09-03` | 50.000 | 50 |
| `gpt6-sol` | `gpt-6-sol` | `2026-09-22` | 50.000 | 50 |
| `gpt6-luna` | `gpt-6-luna` | `2026-09-22` | 50.000 | 50 |

Tutti e quattro risultano `Succeeded`, con SKU **`GlobalStandard` a consumo**,
capacita `50`, filtro **`Microsoft.DefaultV2`** e criterio versioni
**`NoAutoUpgrade`**. Le versioni esplicite evitano cambiamenti automatici
durante i confronti; occorre verificarne il ritiro e aggiornarle manualmente
prima della scadenza. Nessuna capacita provisioned/PTU e stata acquistata.
I limiti riportati sono quelli restituiti da Azure, non throughput garantito
ne un budget o un tetto di fatturazione.

**Global Standard non garantisce elaborazione in Svezia o nell'UE**, anche
se risorsa e progetto sono in Sweden Central. GPT-5.4 e GPT-5.6 rimangono
schede di consigli, non deployment aggiuntivi.

L'account mantiene `disableLocalAuth=true`: usare Microsoft Entra ID, senza
API key. L'endpoint del progetto e:

```text
https://ai-1nn0vaai-demo-swc-6a198800.services.ai.azure.com/api/projects/proj-1nn0vaai-demo
```

Il client `AzureOpenAIClient` dell'app richiede invece questo valore per
`AzureOpenAI:Endpoint`, senza il percorso `/api/projects`:

```text
https://ai-1nn0vaai-demo-swc-6a198800.openai.azure.com/
```

La configurazione locale dell'AppHost usa questi endpoint tramite user-secrets,
con LIVE come modalità predefinita. L'abilitazione e il consenso per invio
restano espliciti: una nuova installazione non avvia inferenze a pagamento.
Il consenso resta manuale per ogni invio e il limite locale e **0.10 USD per run**.

GPT-5 e stato verificato end-to-end con autenticazione Entra, tool calling e
usage del provider. Le tariffe USD per milione di token, verificate il
2026-09-25 tramite Azure Retail Prices API per Sweden Central / Global Standard,
sono **1.25 input, 0.125 input in cache e 10 output**. Una prova su prodotto 83
ha registrato due chiamate, **4292 token input, 237 output e 0.003271 USD**
di costo stimato: e una singola osservazione, non un benchmark o una fattura.

Anche **GPT-6 Sol e LIVE verificato**, con tool `get_product(83)` e usage
provider persistito: **4276 token input, 32 output e 0.0062598 USD**.
Il ledger supporta le due fasce Sol: input/cache letta/cache scritta/output
**2 / 0.2 / 2.5 / 10 USD per milione** fino a 272000 token input inclusi,
**4 / 0.4 / 5 / 15** oltre tale soglia. La cache scritta sostituisce la
tariffa input ordinaria per quei token; conteggi mancanti non diventano zero.
Fonti e dettagli di verifica sono nel [README del RouterHost](src/Shared/Observatory.RouterHost/README.md).

Un deployment configurato non implica disponibilita LIVE: la UI legge
`capabilities.modelCapabilities[].liveReady` dal backend anche per gli override.
Attualmente GPT-5 e Sol sono pronti; Astra e Luna restano non eseguibili in LIVE.
I preset includono un budget esplicito di **0.10 USD per turno** e restano
bloccati se readiness o budget non sono compatibili col backend.
Le due prove riportate sopra hanno configurazioni tecniche diverse: verificano
l'integrazione, **non costituiscono un confronto di qualita o costo fra modelli**.
Lo startup Aspire non crea risorse Azure ne distribuisce modelli.

## Telemetria e limiti delle misure

L'osservabilita e quella di **Aspire**:
[ServiceDefaults](src/Shared/Observatory.ServiceDefaults/Extensions.cs) configura
OpenTelemetry (log, trace, metriche, ASP.NET Core, HttpClient) e registra le
sorgenti native di Microsoft.Extensions.AI e Agent Framework, con le
convenzioni GenAI (modello, token, durata, tool). Il trace e distribuito: in
A2A una sola traccia attraversa `router-a2a` -> `agent-*` -> `shop-*`.

ServiceDefaults aggiunge un solo comportamento,
[AiTelemetryExtensions](src/Shared/Observatory.ServiceDefaults/AiTelemetryExtensions.cs):
sul span nativo della chiamata al modello scrive `observatory.cost.usd`,
`observatory.agent` e `gen_ai.usage.cache_read.input_tokens`, e incrementa la
metrica `observatory.ai.cost` (USD) per agente e profilo.

La UI conserva il ledger per chiamata (token, cache, costo, richiesta logica)
perche serve a confrontare ed esportare le misure: e un dato dell'applicazione,
non un secondo backend di tracing. In A2A lo specialista restituisce le proprie
evidenze nei metadata della risposta A2A; il modello del router riceve solo il
testo della risposta.


Non confondere il primo evento di avanzamento con il primo token della risposta.
Non sommare padre/figli del trace come se fossero chiamate fatturabili distinte.
I reasoning tokens gia compresi nell'output non vanno aggiunti una seconda volta.
Cache, retry e usage mancanti devono essere visibili nella completezza della stima.

### Token consumati e costo per modello

Nel riepilogo del run, anche dentro il bot, sono visibili input, output,
costo stimato e il link **Dettaglio per modello**. La pagina **Token e costi**
raggruppa le chiamate registrate del **run selezionato**, non dell'intera
conversazione, per profilo, modello effettivo, deployment e modalita.
Include tutti gli agenti che usano quel modello, numero di chiamate, token
input/output, cache letta/scritta, reasoning e costo stimato USD.

- Totale token = input + output; cache letta e reasoning sono sottoinsiemi,
  non token da sommare nuovamente.
- Se una chiamata non riporta una metrica, il relativo totale resta
  **Non disponibile**, anche per run falliti o annullati. La tabella per
  chiamata conserva le misure note; la copertura indica quanti costi sono disponibili.
- Le stime sommate sono quelle gia salvate dal backend: cambiare il pricing
  non riprezza lo storico. Le tariffe attuali e i metadati della verifica
  sono consultabili separatamente in **Profili e pricing dichiarati da /config**.
- Costi positivi inferiori a un milionesimo sono mostrati come
  **< 0,000001 USD**, non arrotondati a zero.
- Se usage o tariffe mancano, token e costi restano non disponibili.

Per le misure reali servono LIVE abilitato, endpoint/deployment e capacita
configurati, tariffe applicabili verificate e consenso di spesa. Le tariffe
sono impostazioni backend `Models:<profilo>:Pricing` (`InputPerMillion`,
`CachedInputPerMillion`, `OutputPerMillion`, eventuale
`CacheWriteSurchargePerMillion`, `Currency`, `SourceUrl`, `VerifiedAt`, `Version`).
Nessuna tariffa viene ipotizzata. Il runtime corrente non calcola una stima
quando e configurato un supplemento positivo di cache scritta che non puo
misurare. Il costo e una **stima di inferenza**, non una fattura Azure
ne il costo dell'infrastruttura; durante l'esecuzione i dati sono provvisori.

### Perimetro della prima versione

- `ToolTransport=direct` indica funzioni chiamate direttamente dal framework,
  anziche MCP: **non** significa dominio nel processo API. Inline e Skills
  effettuano chiamate HTTP business reali; A2A delega via HTTP a veri agenti
  remoti con il proprio ciclo modello/tool. MCP non e implementato e viene
  rifiutato esplicitamente, non simulato come una quarta demo.
- Il ledger registra le chiamate al client AI e i messaggi **logici**. Non
  dichiara questi JSON come body HTTP serializzati dall'SDK verso Azure.
- Gli eventi applicativi sono trasmessi via SSE; la risposta degli agenti e
  bufferizzata. Il tempo alla prima risposta non e il TTFT streaming del provider.
- I profili GPT-5/GPT-6 sono controlli sperimentali modificabili, non prompt
  gia ottimizzati e validati su quei deployment.
- LIVE richiede deployment, capacita, tariffe e budget espliciti. Il guard
  interrompe nuove chiamate fra un'inferenza e la successiva: non e un tetto
  di fatturazione imposto dal provider. I batch a pagamento sono disabilitati.

Per le slide usare soltanto evidenze con provenienza dichiarata. Non
generalizzare una singola osservazione come confronto tra modelli.

## Test

Tutti i test sono in [tests](tests) (xUnit) e in
[src/Observatory.Web/tests](src/Observatory.Web/tests) (Vitest): `src` contiene
solo codice applicativo. Nessun test chiama un modello o un servizio esterno.

| Progetto | Cosa verifica |
| --- | --- |
| `Observatory.Core.Tests` | Dominio, catalogo, pricing |
| `Observatory.Shop.Tests` | Le tre API di business in memoria: rotte, errori, header fidati |
| `Observatory.Skills.Tests` | Contratti dei tre skill site e allineamento `SKILL.md` con le procedure degli agenti |
| `Observatory.Agents.Tests` | Istruzioni e tool identici a quelli inviati nelle misure LIVE (golden), round-trip A2A con evidenze, limiti ed esecuzione senza limiti, usage del provider |
| `Observatory.RouterHost.Tests` | Host dei router: architettura dichiarata, anteprima prompt, accounting senza limiti |
| `Observatory.Telemetry.Tests` | Span e metriche native, costo sul span |

```powershell
dotnet test .\AiObservatory.slnx
npm --prefix .\src\Observatory.Web test
npm --prefix .\src\Observatory.Web run build
```

I golden file in [tests/Observatory.Agents.Tests/Golden](tests/Observatory.Agents.Tests/Golden)
sono estratti dalle richieste reali delle misure LIVE: se un refactoring cambia
anche un carattere di prompt o tool, il test fallisce.

Due harness storici in `Observatory.RouterHost.Tests` sono marcati `Skip`:
precedono l'esecuzione solo LIVE e si aspettano run con usage non verificata
completati come "unpriced", mentre il runtime oggi si ferma (fail-closed).
Vanno riscritti con usage del provider prezzata.

Le misure LIVE, a pagamento e sempre esplicite, si ripetono con
[misura-architetture.ps1](presentazione/misura-architetture.ps1).
