# AI Observatory · frontend

React **19.3.0**, React DOM **19.3.0**, Vite **8.3.0**, TypeScript **7.0.2**,
Vitest **5.0.1**, Zod **4.6.5**. Versioni esatte e `package-lock.json`; Node **24.15+ / 24.x**.

## Avvio

Da questa cartella:

```powershell
npm ci
npm run dev -- --port 5173
```

Il dev server ascolta su `0.0.0.0`, accetta il normale `--port` di Vite e usa
`strictPort` per non cambiare silenziosamente la porta assegnata da Aspire.
AppHost deve fornire **tutte** le variabili server-only:

| Variabile | Browser → backend |
|---|---|
| `INLINE_API_URL` | `/api/inline/...` → `${INLINE_API_URL}/api/...` |
| `SKILLS_API_URL` | `/api/skills/...` → `${SKILLS_API_URL}/api/...` |
| `A2A_API_URL` | `/api/a2a/...` → `${A2A_API_URL}/api/...` |

Gli URL sono origini HTTP(S), senza credenziali, path, query o fragment. Per
avvio manuale, copia `.env.example` in `.env.local` e inserisci gli URL dei backend
effettivamente avviati. Variabili mancanti o non valide bloccano l’avvio con un
errore: **nessun target localhost implicito, fixture o catalogo inventato**.
Nessuna variabile `VITE_`, chiave o hostname di servizio viene incorporata nel bundle.
`npm run build` non richiede i backend; `npm run preview` richiede le tre origini.
Health frontend: `GET /health` sia in sviluppo sia nel container.
Su HTTP in LAN, dove `crypto.randomUUID` può non essere esposto, gli identificatori
usano comunque `crypto.getRandomValues`; non si ricorre a `Math.random`.

## Pagine e dati

- Home: `/#/`
- Demo separate: `/#/inline`, `/#/skills`, `/#/a2a`.
- Per ogni demo: `/#/{tech}/chat`, `/examples` (Confronti guidati), `/data` (Dati della demo), `/agents` (Agenti e servizi), `/trace`, `/usage`, `/history`.
- Contratti camelCase da `Observatory.Core\Contracts.cs`, verificati a runtime.
- L’apertura di una demo effettua solo GET: config, prodotti, scenari, storico.
  Nessuna conversazione o chiamata modello viene creata automaticamente.
- La configurazione iniziale usa LIVE senza limiti applicativi. Ogni invio
  richiede modello configurato, `liveReady=true` e consenso esplicito al singolo
  invio. La demo non richiede né mostra un budget.
- La guida legge gli scenari DEVELOPMENT da `/scenarios`, con `main-six-turns`
  selezionato inizialmente e i casi holdout esclusi. Mostra domini/fatti attesi
  come aspettative, non come risultati. Cambiare scenario conserva bozza e storico;
  i pulsanti delle domande preparano testo, non lo inviano.
  “Autorizzo solo bozza sintetica” è deselezionato inizialmente e dopo ogni invio.
- Prodotti da `/products`; card delle risposte correlate tramite `productIds`.
  Thumbnail HTTPS DummyJSON solo nella UI. Chat invia esclusivamente testo e
  configurazione, mai prodotti serializzati, immagini o URL thumbnail aggiunti dal catalogo.

## Bot in basso a destra

### Tre confronti pronti

**Confronti guidati** contiene tre preset: architetture (sei turni), prompt
BAD/GOOD/GOOD con sola ridondanza (due turni di correzione), GPT-5/GPT-6 Sol
(una raccomandazione Catalog). La guida completa con tutti i parametri e i
messaggi e in [ESEMPI-PREIMPOSTATI.md](../../ESEMPI-PREIMPOSTATI.md).

I preset partono esplicitamente da **LIVE**. Ogni variante valida il modello,
le capability configurabili e lo scenario DEVELOPMENT contro le risposte API:
nessun fallback a profili inesistenti o holdout. I pulsanti mostrano eventuali
motivi di blocco.
**Prepara** sostituisce tutta la configurazione (override rimossi, limiti
1500/24, budget LIVE 0.10 USD per turno, full/direct, blocchi espliciti, consenso spento), seleziona la guida
nel bot e prepara solo il primo messaggio. Non invia, non crea conversazioni,
non autorizza LIVE. Gli altri turni si preparano dalla guida nel bot.

Con storico o run selezionati serve prima **Nuova chat**; durante operazioni,
run attivi o invii incerti la preparazione e bloccata. Per un confronto
corretto non cambiare altre impostazioni dopo aver applicato il preset.

Il riepilogo del run mostra anche input/output e costo stimato, con il link
**Dettaglio per modello** alla pagina `/usage`. Questa aggrega le chiamate
del solo run selezionato per modalita, profilo, modello effettivo e deployment.
Gli override per agente sono quindi riflessi dalle chiamate registrate, non
dedotti dalle impostazioni correnti. Un totale con dati mancanti resta
non disponibile; la copertura dei costi e esplicita. Cache letta e reasoning
non vengono sommati una seconda volta. I costi salvati non vengono ricalcolati
con il pricing corrente e gli importi positivi sotto 0,000001 USD non
appaiono come zero. Token e costi sono disponibili solo con usage provider e tariffe.
La disponibilita LIVE usa `capabilities.modelCapabilities`, anche per gli
override, senza dedurre le capacita dal solo deployment. Le tabelle pricing
espongono tariffa completa cache-write, eventuale supplemento e fasce di contesto
del backend; il browser non ricalcola il costo. Lo stato `priced` delle chiamate
LIVE e riconosciuto insieme allo storico `estimated`.

Ogni demo ha un pulsante flottante **Bot · Inline / Agent Skills / Agent-to-Agent**,
inizialmente chiuso, disponibile anche in Agenti, Traccia, Token e
costi e Storico. La home richiede prima di scegliere una demo: non ne seleziona
una automaticamente. La pagina `/#/{tech}/chat`, **Configurazione**,
conserva laboratorio prompt, anteprima e consigli, senza l'elenco prodotti.
La pagina `/#/{tech}/data`, **Dati della demo**, raccoglie il catalogo completo
(ricerca per ID, testo, marca, SKU e tag; dettagli e immagini), tutti gli ordini
sintetici e i testi integrali delle policy con priorita e versione.
Ordini e policy vengono richiesti a `GET /api/{tech}/demo-data` solo aprendo
questa pagina, tramite i rispettivi servizi HTTP. I prodotti rimangono anche
disponibili per risolvere gli ID citati nelle risposte della chat.

L'ispettore mostra anche gli ordini di altri clienti sintetici, distinguendoli
da quelli del cliente della chat e offrendo un filtro dedicato. Questa
visibilita didattica **non concede nuovi permessi agli agenti**. Prezzo di
listino, importo pagato, outlet, consegna, stato e tracking restano distinti.
Il pulsante **Chiedi dell'ordine**, come quello del prodotto, prepara solo
testo e azzera il consenso; non invia e non cambia il cliente assegnato.
**Aggiorna dati** esegue solo letture; un errore nei servizi e esplicito,
ritentabile e non viene trasformato in un dataset vuoto o locale.

Il pannello contiene la **chat reale già esistente**, non una seconda chat o
un secondo `useDemo`: conversazione, selettore storico, percorso guidato dei sei
turni, messaggi/SSE, fonti, prodotti citati, consenso e invio usano lo stesso stato
della demo. Il percorso guidato è espandibile e permette di scegliere anche
le altre domande DEVELOPMENT. Il [set di domande](../../DOMANDE-DEMO.md)
spiega la sequenza, gli output attesi e le differenze fra architetture.
“Chiedi del prodotto” apre il
pannello e prepara una domanda testuale; non la invia e non allega immagini.
Nel widget compaiono solo i prodotti citati dalle risposte, mai l'intero catalogo.

Messaggi e risposta occupano l'area centrale con scorrimento indipendente;
il compositore compatto rimane separato in basso, con modello, budget/opt-in
e consenso LIVE visibili. **Opzioni chat**, chiuso inizialmente, raccoglie nuova
conversazione, storico, guida, configurazione completa e consenso alla bozza
sintetica. Preparare una domanda dalla guida richiude le opzioni.
**Dettagli run** raccoglie snapshot, consumi e controlli tecnici: non precedono
piu la risposta come lunghi riepiloghi aperti. Errori e annullamento rimangono
visibili senza aprire i dettagli; durante l'elaborazione compare il tempo trascorso.
All'apertura o al cambio conversazione viene raggiunto l'ultimo messaggio.
Se si sta leggendo lo storico, gli aggiornamenti non spostano la lettura:
**Vai all'ultima risposta** permette di raggiungere il nuovo contenuto.

- Il dialogo non modale si chiama **Chat con il Router · {demo}**; il pulsante
  espone **Apri chat {demo}**, `aria-expanded` e `aria-controls`.
- Enter/Spazio aprono; il focus raggiunge il messaggio, oppure il titolo se il
  compositore è disabilitato. **Chiudi chat {demo}**, **Riduci chat {demo}** ed
  Escape riportano il focus al pulsante. Escape non interrompe una composizione
  IME né scavalca un evento già gestito dal campo.
- Tab non è intrappolato. Su desktop resta possibile usare la pagina accanto
  al pannello. Su viewport compatti il pannello si riduce quando il focus torna
  alla pagina, senza rubare il focus al controllo scelto.
- Da chiuso il pannello conservato è `hidden`, `inert` e `aria-hidden`: nessun
  controllo interno resta nel percorso Tab. Il log usa `aria-live="off"` e
  gli alert/loading della chat non sono renderizzati; errori e messaggi restano
  nello stato per la riapertura. Il launcher visibile annuncia lo stato soltanto
  a chat chiusa, evitando annunci duplicati o provenienti da controlli nascosti.
- Apertura/chiusura non creano conversazioni, non inviano turni e **non annullano
  il run**. Bozza, messaggi e impostazioni restano disponibili anche cambiando
  pagina nella stessa demo; SSE/polling continuano a seguire il run chiuso nella UI.
- Stato, errori, retry idempotente, riconnessione e **Annulla run** sono disponibili
  nel pannello aperto (controlli tecnici in **Dettagli run**), oppure nella pagina quando è chiuso. Soltanto l'azione
  esplicita “Annulla run” invia una cancellazione.
- Durante un run attivo, anche con SSE connesso, una GET ogni 2,5 secondi
  recupera lo stato autorevole. Un flusso aperto ma silenzioso non impedisce
  quindi di ricevere il risultato finale. Alla conclusione stream e timer
  vengono chiusi; gli errori GET sono espliciti e ritentabili. Non si reinvia
  il turno e non si effettua una nuova chiamata modello.

Il desktop riserva spazio laterale alla chat. Su mobile il pannello usa il
viewport dinamico e le safe area, con margini laterali di almeno 8px sullo spazio
utile anche con scrollbar classiche, senza dimensionare la larghezza in `vw`.
Intestazione e chiusura restano fuori dall'area messaggi
scorrevole; il compositore ha una propria area limitata per non coprire la risposta. Il launcher
non copre il pannello aperto nei viewport compatti. Il link **Apri configurazione
della demo {demo}** riporta alla configurazione della tecnologia attiva.
Cambiare tecnologia mantiene l'isolamento preesistente e apre una nuova UI chiusa,
senza trasferire messaggi, bozza, consensi o override specialisti.

## Quando cambiano le impostazioni del bot

- **Dal prossimo “Invia”**, non al cambio di una select. `DemoWorkspace` conserva
  le impostazioni del prossimo messaggio; modello, profilo prompt e blocchi
  viaggiano insieme nella `configuration` del successivo POST del turno.
  Cambiare opzioni non crea conversazioni, run o chiamate modello.
- **Modello e prompt sono indipendenti.** GPT-5 → GPT-6 nel selettore
  **Profilo modello** non cambia automaticamente BAD/GOOD; BAD → GOOD non cambia
  il modello. Le schede **Consigli dalle guide OpenAI** sono documentazione:
  leggere la scheda GPT-6 non seleziona GPT-6 per il bot.
- **Gli override per agente prevalgono sul modello base.** Restano espliciti e
  visibili anche quando cambia il modello base; non vengono cancellati
  silenziosamente. Il pulsante **Usa il profilo modello per tutti gli agenti**
  li rimuove su richiesta e azzera il consenso alla bozza, senza inviare nulla.
- **La conversazione resta la stessa.** I messaggi precedenti non vengono
  cancellati o rigenerati; la history effettivamente inviata dipende dalla
  strategia Full/Compact. Nel bot, **Impostazioni del prossimo messaggio** mostra
  la scelta corrente prima dell'invio.
- **Il run conserva il proprio snapshot.** `buildTurnRequest` valida e copia
  anche gli oggetti annidati prima di creare/inviare il turno. Il riepilogo
  **Impostazioni registrate nel run** legge soltanto `configuration` dal GET del
  run, mai dalle select correnti. Le impostazioni richieste non sono una prova
  di quale modello sia stato chiamato: le chiamate effettive sono riportate nella Traccia.
  Durante invio/run o esito incerto le opzioni sono bloccate; un retry usa lo
  stesso payload e la stessa chiave, non le impostazioni di un nuovo messaggio.
- **Per confrontare due configurazioni**, apri il bot e premi **Nuova chat**
  per ciascuna prova, ripeti gli stessi messaggi nello stesso ordine e cambia
  una variabile alla volta. La nuova chat conserva modello/prompt/blocchi scelti,
  crea una conversazione soltanto al click esplicito e non avvia un modello.
- LIVE richiede abilitazione backend, modello configurato, budget valido e
  consenso al singolo invio; nessuna spiegazione o controllo nuovo abilita LIVE.

L'anteprima si richiede manualmente per le impostazioni correnti. Cambiare
modello o profilo invalida e annulla quella precedente, anche se una risposta
arriva in ritardo. La lingua e il contenuto delle istruzioni effettive sono
responsabilità del backend e delle skill: la UI mostra il testo restituito
senza tradurlo o riscriverlo, salvo la redazione difensiva dei segreti.
Un diverso modello può ricevere lo stesso testo se profilo e blocchi non cambiano.

Anche una domanda libera come «Mi dici quanti vestiti rossi hai?» è inviata
solo come testo: interpretazione, ricerca e conteggi appartengono al backend.
Non si deducono conteggi globali dalle card visibili né si allegano immagini.
Se si prepara dal catalogo una nuova bozza mentre il primo invio è ancora in
attesa, l'accettazione del primo messaggio non cancella la bozza più recente.

## Laboratorio prompt in chat

La configurazione del prossimo turno mantiene i profili `bad`, `good`, `gpt5` e
`gpt6` e aggiunge cinque checkbox indipendenti: `checklist`, `outputContract`,
`examples`, `redundancy`, `conflictingStyle`. Etichette e descrizioni provengono da
`GET /api/config`, campo `promptBlocks: [{ id, label, description }]`.
All'apertura i cinque blocchi sono **spenti**, conservando le istruzioni di base
dei profili esistenti. I profili `gpt5`/`gpt6` restano controlli didattici non
ottimizzati: le schede di consigli non li riscrivono.

La selezione viaggia nella normale `RunConfiguration`:

```json
{
  "promptBlocks": {
    "checklist": false,
    "outputContract": false,
    "examples": false,
    "redundancy": false,
    "conflictingStyle": false
  }
}
```

Un run storico senza `promptBlocks` viene letto con tutti i valori `false`;
`null` esplicito e valori non booleani sono rifiutati. Non si modifica il dato
storico originale. I preset **Base**, **Dettagliato coerente**, **Ridondante** e
**Stile in conflitto** cambiano soltanto i blocchi e azzerano il consenso alla
bozza. Profilo, modelli, history, limiti e budget restano selezionati, anche dopo
un reset della chat nella stessa demo; il cambio tecnologia mantiene invece
l'isolamento preesistente. Anche il piano degli esperimenti conserva i blocchi.

Ridondanza e contraddizioni riguardano **solo lo stile**, mai autorizzazioni,
fonti, grounding o vincoli di sicurezza. Un prompt lungo ma coerente può essere
utile: la lunghezza non è una misura di qualità. I controlli si disabilitano
durante invio, esecuzione e invio dall'esito incerto. Un retry conserva il payload
originale, inclusa la selezione dei blocchi.

### Anteprima dal backend, non una richiesta modello ricostruita

Solo **Genera anteprima dal server** invia
`POST /api/{tech}/prompts/preview`, inoltrato al relativo `/api/prompts/preview`,
con l'intera `RunConfiguration`. Apertura, checkbox, preset e schede dei consigli
non inviano POST e non avviano run. Il contratto della risposta è:

```text
{ technology, agents: [{ agent, instructions, characterCount }], notice }
```

La UI richiede esattamente gli agenti attivi dichiarati nelle capability:
solo Router per Inline/Skills, quattro agenti per A2A. Mostra il testo del
generatore backend di `ChatOptions.Instructions` e i **caratteri dichiarati**,
non conteggi di token inventati. L'anteprima comprende soltanto base e blocchi
opzionali: istruzioni/risorse del provider nativo delle skill, history,
strumenti e risultati si aggiungono successivamente. La preview non è una
registrazione reale e non compare come span, ledger o chiamata modello.

Sono gestiti caricamento, annullamento, errore e retry esplicito. Ogni cambio di
configurazione/API, avvio di run o unmount annulla la richiesta e invalida il
risultato; risposte ed errori tardivi non sovrascrivono la selezione corrente.
Non viene generato testo di fallback. Eventuali segreti sono oscurati
difensivamente, con avviso che i caratteri si riferiscono al testo backend.

Una preview di una configurazione LIVE strutturalmente valida è consentita
anche prima dell'autorizzazione a eseguire o della configurazione del deployment:
non esegue modelli/tool e non crea stato. I controlli LIVE dell'invio effettivo,
inclusi configurazione, budget e consenso esplicito, restano invariati.

### Consigli documentali, non nuovi modelli eseguibili

Quattro schede accessibili da tastiera (frecce, Home/End) restano indipendenti dal
modello selezionato e non cambiano il registro eseguibile. Sintesi originali in
italiano da fonti OpenAI verificate il **24 settembre 2026**:

- [GPT-6 · Prompting best practices](https://developers.openai.com/api/docs/guides/latest-model/gpt-6-astra.md#prompting-best-practices): guida di famiglia, osservazioni principalmente su Astra; priorità chiare, skill coerenti, struttura esplicita.
- [GPT-5.6 · Prompting guidance for GPT-5.6 Sol](https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6): risultati, prove, vincoli e revisione incrementale delle ripetizioni, preservando sicurezza e requisiti.
- [GPT-5.4 · Verbosity](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.4#verbosity): livello di dettaglio adatto all'attività e distinzione tra verbosità e ragionamento; nessuna ricetta di ottimizzazione aggiunta.
- [GPT-5 · Prompting guide](https://developers.openai.com/cookbook/examples/gpt-5/gpt-5_prompting_guide): istruzioni dirette, confini e condizioni di arresto, eliminazione di ambiguità e contraddizioni.

**Le raccomandazioni Astra non sono verificate per gli alias Sol/Luna della
demo:** non si presume equivalenza di comportamento, deployment o risultati.
Anche l'articolo OpenAI verificato
[Rethinking skills and prompts for GPT-6 Astra](https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra)
distingue istruzioni utili a Sol/Luna da istruzioni adatte ad Astra. Le etichette
dei profili eseguibili non sostituiscono una verifica sul modello effettivo.

La fonte GPT-5.4 corrisponde alla destinazione effettiva del collegamento
Cookbook; non si presume una guida diversa non verificata. I consigli

## Agenti di esecuzione e servizi

Tutti i processi si avviano insieme con Aspire. Catalog, Orders e Returns sono
servizi distinti: ciascuno possiede un’API business HTTP, un agente/API A2A e una
skill di integrazione. La UI distingue **chi esegue il modello** da **quale servizio
riceve una richiesta**:

| Demo | Agenti modello | Percorso verso i tre servizi |
|---|---|---|
| Inline | Solo Router | Istruzioni nel prompt e strumenti → API business HTTP; nessuna skill |
| Skills | Solo Router | Il router carica `catalog`, `orders`, `returns` dai tre `skill-*` site → API business HTTP; nessuna delega A2A |
| A2A | Router, Catalog, Orders, Returns | Il router delega ai tre agenti remoti, ciascuno con proprie chiamate modello |

In Skills il router può leggere anche `references/decision-checklist.md` della
skill Returns. La disponibilità di una skill nello schema **non** ne dichiara il
caricamento: i contatori usano esclusivamente `skill.loaded` del provider nativo
e i suoi argomenti registrati, sempre sotto Router.

`/config.capabilities` è richiesto e validato: `agentNames`, `serviceNames`,
`businessApi` ed `executionTopology` (`router-http`, `router-skills-http`,
`router-a2a`). Solo gli agenti di `agentNames` hanno un selettore modello; i servizi
non diventano override modello. Il cambio demo isola lo stato e azzera il consenso;
un aggiornamento delle capability elimina gli override non più eseguibili. La
validazione prima dell’invio rifiuta override estranei, senza modificare un payload
di retry già registrato.

Le richieste business conservano `agent: "router"` negli eventi
`protocol.request` / `protocol.response`, con `data.protocol: "HTTP"` e
`data.service` uguale al destinatario. Mappa e Traccia mostrano separatamente
agente, protocollo e servizio. A2A mantiene gli agenti remoti e `protocol: "A2A"`;
la telemetria interna resta distinta. Nessuna richiesta HTTP o skill genera righe
nel ledger modello, span, durate o costi sintetici.

## Osservabilità e retry

`EventSource` legge eventi SSE default `message`; deduplicazione per ID e sequenza.
Anche quando l’API restituisce un `eventsUrl` interno, il browser usa il percorso
same-origin del run. Alla chiusura legge run e conversazione dal backend.

Una disconnessione passa a polling **GET** ogni 2,5 secondi con avviso visibile.
Un errore di lettura interrompe il polling e richiede retry esplicito. Riconnettere
SSE non ripete mai POST o chiamate modello. Un invio dall’esito incerto si può
ritentare esplicitamente conservando payload e chiave di idempotenza.
Le richieste fetch hanno un timeout di 30 secondi (5 minuti per i batch);
un timeout non autorizza mai un reinvio automatico. Il consenso alla bozza viene
azzerato anche dopo un retry accettato, un cambio di testo o di conversazione.

I JSON, le esportazioni e i corpi wire annidati sono redatti difensivamente;
non viene usato HTML non sanitizzato. Le misure assenti non diventano zero.
Replay e risultati esperimento mantengono la provenienza restituita dall’API;
un replay non diventa una nuova misura LIVE.
Il frontend richiede il replay con **POST e `Accept: text/event-stream`**,
anche se l’API offre JSON come formato predefinito. Vengono verificati
`X-Original-Run-Id` e `X-Replay-Only: true` e letti incrementalmente i frame originali.
Non si creano conversazioni, turni o run nuovi. La UI riapre il run originale,
mostra la modalità registrata e conserva gli eventi parziali se lo stream si interrompe;
il POST di replay non viene mai riconnesso o ritentato automaticamente.

## Container

**Build context: root del workspace**, non questa cartella:

```powershell
docker build -f src\Observatory.Web\Dockerfile -t observatory-web .
docker run --rm -p 8080:8080 --env INLINE_API_URL --env SKILLS_API_URL --env A2A_API_URL observatory-web
```

Le variabili per `docker run` devono contenere origini raggiungibili **dal container**.
Il Dockerfile copia solo file di `src/Observatory.Web`, compila su `node:24-alpine`
e serve su `nginx:alpine`, porta **8080**. Il template
`nginx/default.conf.template` viene sostituito all’avvio, preservando `$uri`,
`$proxy_host` e le altre variabili nginx. `15-api-origins.envsh` valida le tre
origini prima dell’`envsubst`; SSE non viene bufferizzato, né POST ritentato.
Gli hostname interni non arrivano mai al browser.

## Verifica

```powershell
npm run build
npm test
```

I test usano fixture dichiarate **solo nel test runner**, non nel frontend servito:
contratti, redazione, proxy, deduplicazione/SSE, GET-only reconnect, idempotenza,
consenso, card prodotto e payload senza immagini.
Coprono anche capability, override al cambio tecnologia, mappa dei sei turni con
Router/servizi HTTP oppure agenti remoti A2A e caricamenti nativi nel solo Router.
Per il laboratorio prompt coprono cinque switch, preset/reset, payload preview
e invio, conservazione dei blocchi nei retry/esperimenti, compatibilità dei run
storici, consenso e disabilitazione durante run, annullamento e risposte/errori
obsoleti, separazione API/modelli e le quattro guide con tastiera/fonti.
Esecuzione focalizzata:

```powershell
npm test -- contracts.test.ts PromptLab.test.tsx ModelGuidance.test.tsx api.test.ts Components.test.tsx App.test.tsx
```

Per il widget i test verificano anche tastiera/focus/Escape, conservazione della
bozza, una sola conversazione attraverso le pagine, assenza di invii impliciti,
catalogo separato, prodotti citati, ricezione SSE a pannello chiuso e cancellazione
esplicita distinta dalla chiusura:

```powershell
npm test -- ChatWidget.test.tsx CatalogView.test.tsx App.test.tsx Components.test.tsx PromptLab.test.tsx ModelGuidance.test.tsx
```

Gli smoke storici basati su script PowerShell sono stati rimossi: le verifiche
automatizzate restano nei test xUnit/Vitest. Per Skills, i test richiedono il
caricamento nativo delle tre skill e distinguono quei caricamenti da una semplice
presenza generica di eventi.
