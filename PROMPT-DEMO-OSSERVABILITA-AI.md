# Prompt operativo - Osservare l'AI: qualita, latenza e costi

Preparato il 23 settembre 2026. Aggiornato: Aspire, tre demo e catalogo pubblico.

Questo documento e il prompt da consegnare a un coding agent per IMPLEMENTARE
la demo. Non contiene risultati di benchmark gia eseguiti.

Scelte confermate: React, backend .NET, Microsoft Agent Framework e Aspire
obbligatorio per orchestrazione locale e container. Tre demo distinte: A2A,
Agent Skills e agent-as-tool inline, con router e tre specialisti in ciascuna.
Confronti richiesti: GPT-5 contro GPT-6 e GPT-6 Astra/Sol contro GPT-6 Luna,
esclusivamente su Azure OpenAI / Microsoft Foundry.
Catalogo da API pubblica DummyJSON; ordini e policy sintetici.
Le immagini appartengono soltanto alla UI e non devono arrivare ai modelli.

La demo implementata usa Azure OpenAI LIVE come unica modalita di inferenza.
Le chiamate richiedono deployment configurato, readiness, budget e consenso
esplicito. Non provisionare risorse o avviare misure a pagamento senza approvazione.

Copia il testo dalla sezione "Inizio del prompt" fino alla fine, fonti comprese.
La presentazione definitiva sara un lavoro successivo, basato sulle evidenze
prodotte dall'applicazione, non su percentuali preparate in anticipo.

---

## Inizio del prompt

Agisci come un senior .NET/React engineer con esperienza in Microsoft Agent
Framework, applicazioni multiagente, OpenTelemetry e valutazione di sistemi AI.

Devi IMPLEMENTARE una demo funzionante, osservabile e riproducibile per una
conferenza tecnica di 60 minuti, non soltanto descrivere un'architettura.

Titolo della sessione:

> Osservare l'AI per migliorare la qualita delle risposte e abbassare il costo
> dei modelli.

La tesi da verificare, non da dare per scontata, e:

> Una risposta apparentemente buona puo nascondere molte chiamate, contesto
> ridondante, errori e costi evitabili. Misurare permette di scegliere dove
> intervenire senza peggiorare la qualita.

La demo deve permettere di seguire una conversazione completa, vedere cosa
accade fra gli agenti e ispezionare ogni richiesta effettiva al servizio AI.
Gli esperimenti possono confermare oppure smentire le ipotesi di ottimizzazione.

### 1. Risultato richiesto e confini

Realizza:

1. Un chatbot React/TypeScript utilizzabile dal pubblico.
2. Un backend ASP.NET Core che utilizza realmente Microsoft Agent Framework,
   orchestrato obbligatoriamente da un Aspire AppHost.
3. Tre demo navigabili e avviabili separatamente: agent-as-tool inline,
   Agent Skills native e A2A, ciascuna con router e tre specialisti.
4. Una console di osservabilita nella stessa UI, oltre alle trace OpenTelemetry.
5. Integrazioni reali A2A e MCP e caricamento progressivo di Agent Skills.
6. Esperimenti controllati su modelli, prompt, comunicazione, tool e storico.
7. Un runner di benchmark e un pacchetto di evidenze esportabile.
8. Una scaletta demo da 60 minuti e un runbook per lo speaker.

NON creare ancora la presentazione definitiva. Prepara dati, grafici, trace,
conversazioni e note che permettano di costruirla dopo le misurazioni.

L'applicazione e gli agenti girano come processi locali o container gestiti
dall'AppHost Aspire. Il percorso locale non deve richiedere Docker.
La modalita container deve usare il supporto nativo del .NET SDK
(`dotnet publish /t:PublishContainer`) per i backend, senza Dockerfile .NET
o cartella `containers`. Mantieni un Dockerfile soltanto per React/Nginx.
Documenta configurazione, rete, endpoint, dati persistenti e health check.
Aspire deve pubblicare le immagini locali e attendere la riuscita delle build
prima di avviare i container; le tre demo condividono l'immagine della stessa API.
L'inferenza usa Azure OpenAI LIVE.
Microsoft Agent Framework non richiede, per questa demo, che gli agenti siano
distribuiti come risorse del Foundry Agent Service.

Non aggiungere Kubernetes, servizi cloud di hosting, Redis, database gestiti,
motori vettoriali o pipeline di deployment se non indispensabili e approvati.
Non introdurre Python: runtime e runner sono .NET, frontend e test browser
sono TypeScript. Il recupero dei dati puo essere deterministico da SQLite.

Tutti i modelli usati per router, specialisti, eventuali riassunti ed eventuali
valutatori devono essere OpenAI ospitati su Azure. Nessun fallback automatico
verso api.openai.com, altri provider o modelli locali.

### 2. Verifica iniziale: niente API o compatibilita inventate

Prima di scrivere codice:

- Esamina il workspace e integra eventuale codice esistente senza sovrascriverlo.
- Verifica documentazione e package correnti di Agent Framework per .NET,
  client Azure OpenAI, SDK MCP C#, A2A e supporto delle Agent Skills.
- Distingui funzionalita .NET da esempi disponibili soltanto in Python.
- Preferisci release stabili compatibili. Se serve un package preview, dichiara
  versione, ragione e limitazioni; non nasconderlo.
- Scegli una versione .NET LTS supportata dai package verificati.
- Blocca le versioni effettivamente testate e conserva i lockfile.
- Registra nel README i link ufficiali e la data di verifica.
- Usa le astrazioni del framework per agenti, tool, sessioni e middleware;
  non sostituire il framework con quattro chiamate HTTP ribattezzate "agenti".

Prima di investire nella UI completa, compila una prova minima di compatibilita:
agente locale, delega A2A fra processi, tool MCP e skill caricata on demand.
La compatibilita con il deployment Azure richiede uno smoke test LIVE autorizzato.
Verifica anche che il transport dell'SDK permetta la cattura richiesta:
questo requisito non va scoperto soltanto a implementazione quasi conclusa.

Scopri i deployment Azure gia disponibili soltanto nel contesto autorizzato.
Se la configurazione manca, chiedi i nomi dei deployment, endpoint, regione e
tipo di deployment. Non chiedere di incollare credenziali in chat.
Non provisionare risorse o avviare benchmark a pagamento senza approvazione.

Non chiedere nuovamente di scegliere lo stack o il provider: sono gia decisi.
Chiedi solo informazioni effettivamente bloccanti, una domanda alla volta.

Le verifiche locali senza credenziali possono controllare configurazione e
funzionalita attraverso fixture di test, senza produrre misure di modello.
Le prove LIVE e i benchmark restano non verificati finche non sono eseguiti
contro deployment reali.

### 3. Scenario: ShopLab, assistenza e-commerce

Usa un'azienda fittizia chiamata ShopLab che vende abbigliamento e accessori.

Importa prodotti dall'API pubblica DummyJSON, esplicitamente destinata a
prototipi. Non presentarla come catalogo commerciale o stock reale.
I dati devono essere coerenti e consentire risposte verificabili.
Non usare dati personali, documenti aziendali o ordini reali.
La UI deve indicare "Catalogo pubblico di esempio; ordini e policy sintetici".

Definisci quattro agenti reali:

| Agente | Responsabilita | Strumenti e limiti |
| --- | --- | --- |
| RouterAgent | Comprende l'intento, delega, gestisce richieste multidominio e produce la risposta finale | Delega agli specialisti; non inventa autonomamente dati di dominio |
| CatalogAgent | Prodotti, caratteristiche, disponibilita e alternative | Ricerca e dettaglio prodotti; non modifica ordini |
| OrdersAgent | Ordini, pagamenti effettuati e spedizioni | Lettura ordini autorizzati e tracking sintetico |
| ReturnsAgent | Resi, eccezioni, importi e proposta di soluzione | Lettura policy e creazione di una bozza di reso simulata |

Ogni agente deve avere istruzioni, tool autorizzati, invocazioni e span
identificabili. Un agente configurato ma mai eseguito non basta.
Almeno una conversazione completa deve coinvolgere tutti e quattro.

Il router puo delegare a piu specialisti. Se ReturnsAgent ha bisogno dei fatti
di un ordine, il router acquisisce quei fatti da OrdersAgent e li passa
esplicitamente nel contratto di delega: non serve una rete incontrollata
di agenti che si chiamano reciprocamente.

Questo e un pattern supervisor/agent-as-tools: il router mantiene il controllo
e ricompone il risultato. Non confonderlo con l'orchestrazione Handoff del
framework, che puo trasferire la responsabilita della conversazione.

Le dipendenze sono rispettate: una decisione sul reso non parte prima di avere
i fatti necessari. Catalogo e altre ricerche indipendenti possono essere
parallelizzati in un esperimento separato.

Il router deve essere un agente AI, non uno switch su parole chiave.
La validazione di permessi, importi, identificativi e limiti operativi deve
invece essere deterministica lato backend.

### 4. Catalogo pubblico, immagini UI-only e verita di riferimento

Prepara almeno:

- 30 prodotti importati da DummyJSON, preferendo abbigliamento e accessori,
  con titolo, descrizione, categoria, prezzo, stock, SKU e riferimenti immagine.
- 50 ordini con clienti sintetici, righe, sconti, importo pagato e spedizioni.
- 10 identita cliente sintetiche, con isolamento dei rispettivi ordini.
- 12 documenti o sezioni di policy con ID, versione, validita e precedenze.
- 24 scenari valutabili, con casi semplici, complessi e multi-turno.

Implementa un import esplicito dall'API https://dummyjson.com/products,
con proiezione dei soli campi necessari e validazione del contenuto.
Non importare recensioni, email dei recensori, QR code o dati utente.
Registra endpoint, data di acquisizione, ID originali e hash dello snapshot.
I benchmark usano uno snapshot congelato, non un catalogo che cambia durante A/B.
Un errore di import non va nascosto: mostra l'errore e l'eventuale snapshot
precedente usato su scelta esplicita, con la sua data.

Definisci due DTO separati: prodotto UI e fatto prodotto per gli agenti.
Solo il DTO UI contiene thumbnail/image URL. I tool AI non devono restituire
URL delle immagini, base64, contenuti image_url o allegati multimodali.
La UI risolve product ID in immagini dopo la risposta del backend.
Verifica con test i payload di tutti gli agenti, compresi quelli via A2A.
Mostra attribuzione della fonte; non scaricare o redistribuire immagini binarie.

Usa un seed riproducibile e un orologio applicativo controllato.
Per la storia principale fissa asOf a 2026-09-23T10:00:00Z.
Le policy sono esclusivamente regole fittizie della demo, non indicazioni legali.

Costruisci questa fixture principale in modo coerente:

- Ordine ORD-1042, appartenente al cliente autenticato della demo.
- Prodotto pubblico ID 83: Blue & Black Check Shirt,
  SKU MEN-FAS-BLU-083, verificato nello snapshot.
- Prezzo nel catalogo acquisito: 29.99; importo pagato nell'ordine sintetico:
  19.99. La demo usa USD come convenzione esplicita, non come quotazione reale
  di un negozio. Non alterare silenziosamente i prezzi ricevuti dall'API.
- Ordine contrassegnato outlet; consegna il 5 settembre 2026.
- Policy standard: reso per ripensamento entro 30 giorni.
- Eccezione outlet: ripensamento entro 14 giorni dalla consegna.
- Eccezione difetto: entro 60 giorni, anche per outlet.
- Rimborso eventuale basato sull'importo pagato, non sul listino.
- Nessuna operazione su pagamenti reali: soltanto una bozza persistita localmente.

Gli ID delle policy devono comparire nelle evidenze delle risposte.
Consegna e data corrente devono produrre esattamente 18 giorni trascorsi.
Le eccezioni e le loro precedenze devono essere presenti nei dati di dominio,
non nascoste soltanto nei test.

Includi casi con:

- Ordine inesistente o di un altro cliente.
- Dato mancante che richiede una domanda, non una supposizione.
- Policy scaduta e policy corrente.
- Correzione dell'utente di un'informazione data in precedenza.
- Richiesta ambigua, multidominio o fuori ambito.
- Errore o timeout di un tool.
- Testo recuperato che contiene istruzioni non attendibili.
- Richiesta di una bozza gia creata, per verificare l'idempotenza.

Per ogni scenario conserva:

- Messaggi utente e ordine dei turni.
- Fatti e decisioni attesi, non soltanto una risposta testuale da imitare.
- Policy e fonti ammesse.
- Tool o deleghe necessarie/proibite.
- Regole di valutazione e severita degli errori.

Separa un insieme di sviluppo da un holdout prima di ottimizzare i prompt.
Non inserire risposte attese, annotazioni del valutatore o etichette del test
nei prompt, nelle skills o nei risultati dei tool.

### 5. Esecuzione LIVE e replay delle evidenze

L'unica modalita di inferenza e LIVE:

| Modalita | Modello | Uso corretto |
| --- | --- | --- |
| LIVE | Deployment Azure OpenAI reale | Misurazioni e dimostrazioni con consumo reale |
| REPLAY | Riproduzione di una precedente run LIVE | Consultazione di evidenze originali, con provenienza visibile |

Un errore Azure deve essere mostrato come errore Azure; non eseguire fallback
automatici a un'altra modalita o a un provider alternativo.

In REPLAY distingui:

- Durata, token e costo stimato della run originale.
- Tempo di riproduzione attuale e nuove chiamate AI effettuate, normalmente zero.

Non mescolare queste modalita nelle statistiche. Non costruire registrazioni
che sembrino provenire dal servizio se non esiste la relativa run LIVE.

### 6. Architettura e persistenza

Preferisci una soluzione piccola, organizzata per responsabilita:

- Aspire AppHost: punto di avvio unico e dashboard osservabile.
- React/TypeScript: chat, pannello esperimenti, trace explorer e report.
- Tre risorse API Aspire distinte: demo-inline, demo-skills e demo-a2a.
  Possono usare lo stesso eseguibile parametrizzato, non tre copie del codice.
- ASP.NET Core: API applicativa, router e coordinamento delle run.
- Libreria .NET condivisa: agenti verticali, contratti e servizi di dominio.
- Host .NET per esporre gli stessi specialisti attraverso A2A.
- Host MCP C# per esporre gli stessi tool di dominio.
- Runner .NET per scenari, benchmark ed export.
- SQLite per dataset, sessioni, run, ledger e riferimenti ai payload.
- OpenTelemetry e Aspire Dashboard, con correlazione fra tutti i processi.

La home deve presentare tre demo, con navigazione e stato distinti:

1. Inline: tre specialisti esposti al router come tool in-process.
2. Skills: stessi quattro agenti e tool, con istruzioni specialistiche caricate
   progressivamente tramite il provider nativo Agent Skills.
3. A2A: router che delega agli stessi specialisti attraverso HTTP/A2A reale.

La demo Skills continua a usare delega in-process: cambia il caricamento delle
istruzioni, non inventa un protocollo di trasporto chiamato Skill.
MCP rimane un asse aggiuntivo per gli strumenti e non sostituisce queste demo.

L'AppHost configura service discovery, endpoint e variabili delle risorse.
Il browser chiama API relative tramite proxy, non hostname interni dei container.
Prevedi una selezione esplicita processi/container nell'AppHost e mantieni
equivalenti i percorsi applicativi. Le credenziali Azure restano solo backend.
Docker non attivo deve produrre un prerequisito chiaro, non bloccare il locale.

Non duplicare la business logic fra esecuzione locale, MCP e A2A.
Un solo host A2A puo esporre piu agenti distinti se il framework lo supporta:
non creare tre codebase quasi identiche.

Persisti conversazioni, configurazioni delle run e risultati. Il riavvio
dell'applicazione non deve cancellare le evidenze gia raccolte.

Usa streaming verso React, per esempio SSE, con un contratto tipizzato per:
avanzamento, delega, tool call, testo finale, usage disponibile ed errore.
La riconnessione al flusso non deve rieseguire la domanda e spendere altri token.

Supporta cancellazione, timeout, ID idempotenti per invio messaggi e isolamento
delle sessioni. Gestisci esplicitamente turni concorrenti della stessa chat.
Mantieni i dettagli di telemetria fuori dal contesto inviato ai modelli.

### 7. MCP, Agent Skills e A2A: tre assi indipendenti

Implementa le tre demo separate richieste, senza descrivere MCP, Skill e A2A
come protocolli equivalenti. All'interno degli esperimenti mantieni distinti
i fattori che possono coesistere.

| Asse | Variante A | Variante B | Domanda dell'esperimento |
| --- | --- | --- | --- |
| Delega fra agenti | Invocazione locale | A2A via HTTP | Quanto costa il trasporto e la gestione remota a parita di lavoro? |
| Accesso ai tool | Funzioni .NET dirette | MCP | Quale overhead introduce il protocollo per lo stesso tool? |
| Istruzioni specialistiche | Caricamento eager | Agent Skills on demand | Quanto contesto si risparmia e quante chiamate si aggiungono? |

La configurazione locale di riferimento deve gia avere quattro agenti.
Non spacciare la sostituzione di tre agenti con una funzione per
un miglioramento del protocollo A2A.

#### 7.1 Delega A2A

Usa un'implementazione reale del protocollo, con client e server compatibili.
Non chiamare A2A una REST API personalizzata che riceve una stringa.

Mostra Agent Card, capacita dichiarate, messaggi, eventuali task/artifact e
gestione degli stati effettivamente supportati dalla versione scelta.
Documenta versione del protocollo e binding di trasporto.

Mantieni la stessa implementazione degli specialisti nei due percorsi.
Normalizza il risultato di dominio senza cancellare dal trace il messaggio
originale A2A. Registra dimensione e durata dei messaggi.

La documentazione corrente comprende una migrazione all'A2A SDK v1 e tutorial
con API di hosting precedenti. Scegli un insieme coerente di package/esempi e
testa discovery e binding. Versione del package/SDK, versione wire del protocollo
e versione applicativa nella Agent Card sono tre informazioni diverse.

Definisci un contratto di delega contenente almeno:

- conversationId, turnId, runId e un ID di delega.
- Compito richiesto e domanda corrente.
- Identificativi e fatti necessari, con fonti.
- Vincoli e formato del risultato.
- Versione dello schema e del contesto.

La variante ottimizzata non deve passare per abitudine tutta la conversazione,
tutte le istruzioni degli altri agenti o l'intero catalogo.
La variante full-history deve essere un esperimento distinto dal trasporto.

Gestisci mapping di context/task ID remoti, isolamento cliente, timeout,
cancellazione, errori e correlazione W3C del trace dove supportata.
Se una capacita non e disponibile, dichiarala; non simularla silenziosamente.

#### 7.2 Tool MCP

Usa il client/server ufficiale MCP C# con Streamable HTTP verificato nella
versione pubblicata scelta. Seleziona esplicitamente il trasporto per evitare
che autodetection o fallback a SSE legacy alterino il confronto.
La prima implementazione deve essere MCP consumato dal backend .NET, non un
remote MCP tool gestito dentro il servizio Azure.

Flusso da rendere visibile:

1. Il backend scopre o seleziona i tool MCP.
2. Il client AI invia al modello le definizioni dei tool selezionati.
3. Il modello restituisce una richiesta di tool.
4. Il backend esegue la chiamata tramite MCP.
5. Il risultato scelto dall'applicazione entra nella richiesta AI successiva.

Azure non deve tentare di raggiungere un server MCP su localhost.

Esponi strumenti come ricerca prodotti, lettura ordine e lettura policy.
Usa gli stessi servizi di dominio e contratti della variante a funzioni locali.
Gli schemi visibili al modello devono essere equivalenti; registra eventuali
differenze introdotte dall'adapter.

Separa discovery, inizializzazione, riuso della sessione e chiamata del tool.
Un envelope JSON-RPC non diventa automaticamente parte del prompt del modello.
Un server MCP non implica automaticamente una chiamata LLM aggiuntiva.

#### 7.3 Agent Skills

Qui "Skill" significa Agent Skill con SKILL.md, metadati e istruzioni/risorse
caricate quando servono. Non significa una vecchia funzione chiamata skill,
ne la sola descrizione di una capacita nella Agent Card A2A.

Prepara skills per catalogo, ordini e resi con:

- Nome e descrizione brevi.
- Istruzioni procedurali.
- Riferimenti a risorse di dominio.
- Versione e hash del contenuto.
- Regole chiare su quando caricarle.

La documentazione corrente descrive supporto .NET nativo attraverso
AgentSkillsProvider e AgentSkillsProviderBuilder: parti da quello e verifica
la versione del package che lo rende disponibile. Non costruire un loader
custom se quello nativo soddisfa il requisito.
Solo se il supporto manca nella versione compatibile scelta, implementa un
loader applicativo conforme al sottoinsieme documentato del formato e
dichiaralo esplicitamente nel README e nella UI.
Non spacciare codice custom per una funzionalita nativa.

Non eseguire script arbitrari contenuti nelle skills. Limita i percorsi
leggibili alle risorse della demo e impedisci path traversal.

Confronta:

- Eager: stesse istruzioni caricate tutte all'inizio.
- On demand: metadati iniziali e contenuto della skill caricata quando necessario.

Registra cosa il framework aggiunge davvero a ogni richiesta. Conta anche il
costo della selezione/caricamento e l'eventuale crescita delle skills accumulate
nello storico. Il caricamento on demand puo peggiorare latenza o costo.

Le skills non sono un trasporto di messaggi tra agenti e non devono eliminare
i tre specialisti dal confronto principale.

### 8. Registro modelli e configurazioni riproducibili

Definisci profili espliciti richiesti dall'utente, mantenendo configurabili
i nomi reali dei deployment:

- gpt5: baseline GPT-5.
- gpt6-astra: GPT-6 Astra.
- gpt6-sol: GPT-6 Sol.
- gpt6-luna: GPT-6 Luna.

I confronti obbligatori sono GPT-5 contro ciascun GPT-6 configurato,
GPT-6 Astra contro Luna e GPT-6 Sol contro Luna.
Non sostituire questi nomi con GPT-4, altre famiglie o provider.
Non assumere che Astra/Sol/Luna siano sinonimi di costoso/veloce/economico.
Verifica ID, versione e disponibilita Azure prima delle esecuzioni LIVE.
Un modello senza deployment resta configurabile ma non eseguibile in LIVE.
Non generare latenze, token o ranking fittizi per far vincere un modello.

Registra per ogni deployment:

- Provider/servizio Azure e nome del deployment.
- Famiglia, ID e versione del modello verificati.
- Regione e tipo: regional, global, data zone, provisioned o altro verificato.
- API e versione/modalita utilizzate.
- Capacita: tool, streaming, structured output, reasoning e caching.
- Parametri supportati e impostazioni effettivamente inviate.
- Riferimento al listino applicabile.

Non assumere che il nome del deployment coincida con il nome del modello.
Confronta la configurazione attesa con i metadati restituiti dal servizio.
Se qualcosa non e noto, usa "unknown", non un valore plausibile.

Una run deve contenere uno snapshot immutabile dell'intera configurazione:
modelli per agente, prompt, skills, schema tool, strategia storico, trasporti,
limiti, retry, dataset, listino e versione applicativa.

Per il primo confronto cambia il modello di un solo agente, mantenendo fisso
il resto. Il confronto "tutto premium" contro "mix ottimizzato" viene dopo
ed e dichiarato come cambiamento architetturale composto.

Non inviare temperature, top_p, reasoning_effort o altri parametri che il
deployment non supporta. Non cambiare API, parametri o regione di nascosto.
Un errore di compatibilita va spiegato e risolto, non ignorato.

### 9. Prompt lab: pessimi, corretti e specifici per modello

I prompt devono essere file versionati, editabili e confrontabili con un diff.
Ogni run registra testo effettivo, versione e hash.
Mantieni separati prompt di routing e prompt dei tre specialisti.

Prepara almeno quattro profili:

- BAD_BASELINE.
- GOOD_GENERAL.
- MODEL_ADAPTED_GPT5.
- MODEL_ADAPTED_GPT6, con specializzazioni Astra/Sol/Luna solo se giustificate
  dalle guide ufficiali delle versioni scelte.

BAD_BASELINE e disponibile solo in modalita laboratorio con dati sintetici.
Non indebolire controlli backend o autorizzazioni per far sembrare il prompt
piu importante di quanto sia.

Esempio originale di prompt volutamente difettoso per ReturnsAgent:

> Sei un esperto di tutta l'assistenza clienti. Cerca di accontentare sempre
> il cliente. Rispondi subito e in modo molto completo. Se qualche dettaglio
> manca, proponi comunque una soluzione plausibile. Le regole di reso sono
> generalmente semplici. Includi tutte le informazioni disponibili.

Difetti da poter osservare, senza garantirne l'esito:
ruolo troppo ampio, assenza di grounding, nessuna gestione dell'incertezza,
nessuna distinzione tra listino e pagato, nessuna precedenza delle policy,
verbosita eccessiva e uso dei tool non definito.

Esempio di versione corretta:

> Gestisci esclusivamente richieste di reso per il cliente autorizzato.
> Recupera i fatti dell'ordine e le policy applicabili usando i tool.
> Distingui ripensamento e difetto, applicando le precedenze esplicite
> nelle policy. Non inventare date, importi, autorizzazioni o eccezioni.
> L'importo rimborsabile deriva dal pagamento, non dal prezzo di listino.
> Se manca un dato decisivo, chiedi un chiarimento mirato.
> Non eseguire pagamenti. Una bozza di reso richiede conferma e validazione
> deterministica del backend.
> Restituisci esito, breve motivazione verificabile, fonti e prossimo passo.

I vincoli di formato e le regole del dominio devono essere equivalenti nelle
varianti "buone": non cambiare il problema per favorire un modello.

#### Adattamento alle guide del modello

Prima di creare ciascuna variante MODEL_ADAPTED:

1. Cerca la guida ufficiale del modello effettivamente scelto.
2. Registra URL, data di consultazione e modello/versione cui si applica.
3. Spiega quali istruzioni o parametri cambi e perche.
4. Tratta le raccomandazioni come ipotesi da verificare sul nostro dataset.

Il confronto attuale riguarda GPT-5 e GPT-6, non una sostituzione con GPT-4.1.
Non inventare differenze di prompting fra queste famiglie: cerca la guida
applicabile e associa ogni modifica a una raccomandazione verificata.
Se manca una guida specifica per una variante, dichiara il limite e usa il
prompt generale come controllo, non chiamarlo "ottimizzato" senza evidenze.

Prepara istruzioni operative esplicite: quando usare i tool, quali evidenze
servono, come gestire dati mancanti, formato, criteri di successo e arresto.
Non chiedere ragionamento interno. Aggiungi esempi o configura reasoning
soltanto quando supportato e motivato dalle guide e dal set di sviluppo.

Non usare la scorciatoia "reasoning = prompt sempre corto" oppure
"modello costoso = migliore". Non riempire un prompt di testo inutile soltanto
per costruire artificialmente una grande differenza di token.

Implementa un esperimento 2 x 2:

| | Prompt GPT-5-adapted | Prompt GPT-6-adapted |
| --- | --- | --- |
| GPT-5 | Misura | Misura |
| GPT-6 selezionato | Misura | Misura |

Mantieni invariati task, dati, tool e criteri di successo.
Ripeti i confronti pertinenti per Astra, Sol e Luna configurati.
Il risultato puo mostrare differenze grandi, piccole o nessuna differenza.
Le slide future devono raccontare quello che emerge.

### 10. Storico multi-turno e deleghe

Supporta almeno:

- FullHistory: lo storico applicativo completo valido per quel contesto.
- CompactHistory: fatti strutturati con fonti, eventuale riassunto e turni recenti.

Mostra, per ogni turno e agente:

- Messaggio corrente.
- Storico conservato dall'applicazione.
- Sottoinsieme effettivamente passato allo specialista.
- Istruzioni e skills aggiunte.
- Risultati dei tool reinseriti.
- Contenuto ripetuto rispetto alla richiesta precedente.

Nella prima baseline preferisci storia esplicita lato applicazione e parametri
di storage documentati, cosi il traffico e piu facile da spiegare.
Valida la scelta sull'API e sui modelli realmente disponibili.

Non appiattire arbitrariamente risposte strutturate in sole stringhe.
Preserva associazioni fra tool call e risultati e tutti gli item richiesti
dalla specifica dell'API, incluse fasi o item opachi di reasoning se applicabili.
Un item cifrato deve rimanere opaco: non e chain-of-thought leggibile.

Se provi previous_response_id o conversazioni mantenute dal servizio:

- Fallo come esperimento separato.
- Mostra il riferimento effettivamente trasmesso.
- Distingui body HTTP ridotto e contesto disponibile lato servizio.
- Non sostenere che lo storico sia gratuito perche non appare nel body.
- Non ricostruire come "esatto" uno stato interno che non puoi osservare.

Il riassunto, se generato da un modello, deve usare Azure OpenAI ed essere una
chiamata tracciata e fatturata nel totale della conversazione.
Misura il punto in cui il suo costo iniziale viene eventualmente recuperato.

Conserva correttamente correzioni dell'utente, order ID, importo pagato,
eccezioni, negazioni, stato di conferma e riferimenti alle policy.
Mostra anche un caso in cui una compressione aggressiva perde un fatto
importante e peggiora la risposta.

### 11. Osservabilita: la funzionalita principale della demo

Non basta avere log testuali con la risposta finale.

Usa OpenTelemetry e gli hook/instrumentation del framework dove disponibili.
Non duplicare gli span automatici. Aggiungi span applicativi soltanto per
operazioni non gia coperte: run, turni, deleghe, caricamento skill ed export.

Struttura desiderata, adattata agli span reali dell'SDK:

```text
conversation
  turn
    router invocation
      model request / attempts
      delegation
        local invocation OR A2A client/server
          specialist invocation
            model request / attempts
            direct tool OR MCP client/server
            model request / attempts
      final model request / attempts
```

Una invocazione di agente puo produrre piu richieste al modello.
Il numero di agenti non deve essere confuso con il numero di chiamate LLM.

Propaga correlazione fra API, router, host A2A e MCP. Se la gerarchia non puo
essere propagata direttamente, usa collegamenti espliciti e documentati.
Non perdere le chiamate effettuate dentro gli agenti remoti.

Mantieni un ledger persistente delle richieste AI, distinto dagli span:
le metriche aggregate del tracing non sono il registro contabile.

Registra almeno:

- experimentId, scenarioId, repetition e runId.
- conversationId, turnId, agentId e delegationId quando presenti.
- traceId, spanId, logicalCallId e attemptId.
- Provenienza LIVE o REPLAY.
- Deployment, versione verificata, API e configurazione.
- Timestamp UTC, durate monotone, esito, errore e ragione di incompletezza.
- Prompt/skill/tool schema hash e riferimenti ai payload.
- Request/response ID del provider se disponibili.
- Usage originale e usage normalizzato, indicando campi mancanti.
- Versione del listino, costo stimato e completezza della stima.

Usa gli ID ad alta cardinalita nei trace/record, non come label illimitate
nelle metriche aggregate.

### 12. Wire inspector: cosa attraversa ogni confine

Implementa un pannello "Cosa viene inviato al modello" basato su cio che
l'SDK ha realmente serializzato e inviato, non su un prompt ricostruito a mano.

Verifica il punto di intercettazione nel transport/pipeline dell'SDK scelto.
Un middleware che vede messaggi prima della serializzazione non e sufficiente
per dichiarare di aver catturato il body HTTP definitivo.

Cattura in modo controllato:

- Destinazione/operazione, senza credenziali.
- Body della richiesta dopo la serializzazione dell'SDK.
- Istruzioni, messaggi/input item, schemi tool e opzioni del modello.
- Chunk/eventi della risposta necessari a ricostruire output e usage.
- Risposta finale, tool call e metadati utili.
- Tentativi HTTP distinti e relativi errori quando osservabili.

Non leggere tutto lo stream prima di consegnarlo al chiamante: la cattura non
deve disattivare lo streaming o falsare il time-to-first-token.
Imponi limiti di dimensione e registra esplicitamente qualsiasi troncamento.
Una cattura incompleta non puo soddisfare il requisito "conversazione completa".

Offri tre viste collegate:

1. **Application view:** messaggi utente, deleghe, fatti e risultati.
2. **Protocol view:** messaggi effettivi MCP/A2A, dimensioni e tempi.
3. **Model request view:** body effettivo diretto ad Azure OpenAI.

Per ogni contenuto mostra la provenienza: istruzione base, messaggio utente,
storico, skill, risultato tool o risultato di un altro agente.

Rendi esplicite queste distinzioni:

| Elemento | Viaggia nel protocollo? | Arriva automaticamente al modello? |
| --- | --- | --- |
| Envelope e ID MCP | Si, fra client e server MCP | No: dipende dall'adapter e dal contenuto inserito nel prompt |
| Schema del tool esposto al modello | Deriva anche dalla discovery | Si, se incluso nella richiesta AI |
| Risultato di un tool | Si nel percorso del tool | Solo cio che l'applicazione reinserisce |
| Agent Card / task / artifact A2A | Nel protocollo A2A secondo il binding | Non necessariamente il documento o envelope completo |
| Contenuto di una skill | Letto dal runtime | Si, quando viene inserito nel contesto |
| Trace e costi dell'app | Nel sistema di osservabilita | No, salvo scelta esplicita, da evitare nella demo |

Misura byte UTF-8 dei payload applicativi con definizione chiara: non chiamarli
"byte fisici sulla rete" se non includi header, compressione, framing e TLS.

Per questa architettura l'etichetta precisa e:

> Inviato ad Azure OpenAI, modello OpenAI.

Non disegnare automaticamente un passaggio dei prompt verso api.openai.com.
Il servizio ospitante e Azure. Cita la documentazione Microsoft sulla gestione
dei dati, senza dedurre dal trace garanzie ulteriori su retention o residenza.

La "conversazione completa" significa tutti i messaggi, tool e scambi osservabili
dalla nostra applicazione. NON include ragionamento interno nascosto,
prompt interni del provider, attivazioni o stato del servizio non esposto.

### 13. Privacy della telemetria

La cattura dettagliata del contenuto e esplicita e limitata al laboratorio.
Abilitala per dati sintetici in locale; non attivarla per dati reali per default.

- Escludi Authorization, api-key, cookie, token di autenticazione e segreti.
- Applica redazione prima della persistenza e dell'export.
- Distingui body originale osservato e sua rappresentazione sanificata.
- Segnala redazioni/troncamenti e il relativo effetto sulla ricostruzione.
- Non inserire payload completi nei normali span se il backend li tronca:
  conserva un riferimento al content store locale protetto.
- Non attivare export cloud dei prompt senza consenso.
- Prevedi cancellazione mirata delle run, non cancellazioni indiscriminate.
- Impedisci che Markdown, tool output o payload esportati eseguano script nella UI.

Per l'accesso Azure usa preferibilmente Microsoft Entra ID, oppure segreti
backend tramite configurazione sicura locale. Nessuna chiave nel frontend,
negli screenshot, nel repository o nei pacchetti per le slide.

### 14. Token e costo: contabilita verificabile

La fonte primaria dei token e usage restituito dal provider per ogni richiesta.
Non calcolare la spesa reale con caratteri/4 o conteggi della sola risposta.

Conserva almeno, quando disponibili:

- Input totale.
- Input letto dalla cache.
- Eventuali token di scrittura cache.
- Output totale.
- Reasoning tokens come sottoinsieme/attributo secondo la semantica dell'API.
- Token totali e altri meter fatturabili del modello.

Un campo assente e sconosciuto, non necessariamente zero.
Non sommare due volte usage ripetuto in chunk, middleware e span.
Non sommare il conteggio cumulativo di ogni evento streaming.
Normalizza conservando sempre il record originale del provider.

Per una tariffa testuale con soli tre prezzi, usa questa formula:

```text
estimated_cost_usd =
  ((input_tokens - cached_input_tokens) * input_price_per_million
   + cached_input_tokens * cached_input_price_per_million
   + output_tokens * output_price_per_million) / 1_000_000
```

La formula e valida SOLO se descrive tutti i meter del deployment scelto.
Non aggiungere nuovamente i reasoning tokens quando sono gia inclusi
nell'output fatturato.

Il motore di pricing deve gestire anche:

- Tariffe diverse per modello/versione, regione e tipo di deployment.
- Soglie di contesto lungo quando previste dal listino.
- Eventuali costi di cache write, applicati secondo la semantica documentata
  e senza doppio conteggio.
- Differenze fra tariffe pay-as-you-go e capacita provisioned.
- Tariffe concordate dall'utente, se fornite, distinte da quelle pubbliche.

Non trattare PTU come un listino per-token. Per questa demo preferisci
pay-as-you-go; per PTU servirebbe un modello di allocazione separato.

Usa SOLO prezzi Azure pertinenti al deployment. Il listino dell'API diretta
OpenAI non rappresenta automaticamente il costo Azure.

Mantieni un catalogo prezzi versionato con valuta, unita, fonte URL,
data di verifica, condizioni e metodi di applicazione.
Se il sito mostra un prezzo non disponibile o un trattino, non trasformarlo
in zero e non inventarlo: richiedi una tariffa verificabile.

In UI chiama il risultato "costo stimato di inferenza", non "fattura Azure".
Gli importi monetari usano decimal; conserva precisione prima dell'arrotondamento.

Stati minimi:

- CompleteEstimate: usage e tariffe necessari sono disponibili.
- PartialEstimate: alcuni componenti o tentativi non sono quantificabili.
- Unpriced: manca una tariffa applicabile.

Per gli ultimi due mostra il subtotale noto e le parti mancanti, senza farlo
apparire come un costo totale comparabile.
Un timeout o retry senza usage non deve risultare automaticamente gratuito.

Separa logicalCallId e attemptId: ritentativi reali non vanno deduplicati come
duplicati di telemetria, mentre lo stesso evento registrato due volte si.
Registra quando gli hook non permettono di osservare un tentativo intermedio.

Mostra costi per richiesta, agente, turno, conversazione ed esperimento.
Le chiamate di riassunto/routing fanno parte del costo del chatbot.
Gli eventuali judge hanno un totale separato, incluso poi nel costo del
laboratorio. Non sommare padre e figli se rappresentano lo stesso consumo.

La suddivisione token per "istruzioni / storico / tool / skill" e una stima
diagnostica con tokenizer verificato, NON una fattura ufficiale per segmento.
Mostra la differenza fra somma delle stime e usage reale; non forzarle a coincidere.

### 15. Budget, quote e operazioni affidabili

Prima di ogni batch LIVE:

- Presenta scenari, ripetizioni, modelli e numero stimato di chiamate.
- Presenta la stima di costo e le sue incertezze.
- Richiedi approvazione di un budget esplicito.
- Non lanciare un batch non prezzabile senza una decisione esplicita dell'utente.

Il default del runner e dry-run, non un benchmark costoso.
Imponi limiti configurati a durata, chiamate LLM, tool, deleghe, profondita e
output. Evita loop router-specialista e retry a piu livelli incontrollati.

Rispetta rate limit e Retry-After. Registra retry, backoff e cancellazioni.
Per parallelismo e budget considera le chiamate gia in corso.
La soglia locale limita l'ammissione di nuove richieste: non promettere un
tetto di fatturazione esatto quando il provider ha gia accettato lavoro
o mancano usage intermedi.

Non ignorare le failure per rendere piu belli i grafici.

### 16. Interfaccia da conferenza

Realizza una UI leggibile proiettata, non un pannello di soli JSON minuscoli.

Pannelli richiesti:

1. **Chat:** messaggi, risposta in streaming, fonti, stato e costo del turno.
2. **Agent map:** router e tre specialisti, con deleghe illuminate durante la run.
3. **Waterfall:** durata e dipendenze di chiamate AI, tool, skill e trasporti.
4. **Request inspector:** payload e diff tra turni/configurazioni.
5. **Token & cost ledger:** righe per richiesta e aggregazioni verificabili.
6. **Experiment comparison:** qualita, latenza, costo e failure per variante.
7. **Speaker mode:** scenari pronti, preset, reset sicuro ed export.

Nel controllo esperimenti separa:

- Modello per agente.
- Profilo prompt.
- Delega locale/A2A.
- Tool diretti/MCP.
- Skills eager/on demand.
- Strategia storico.
- Limiti di output/reasoning supportati.

Non applicare cambiamenti di configurazione nel mezzo di una run.
Per un confronto crea sessioni indipendenti da uno stesso scenario.

Preset utili:

- Naive baseline.
- Prompt corretto, stesso modello.
- Modello diverso, stesso prompt.
- Prompt adattato al modello.
- Delega con contesto compatto.
- Tool via MCP.
- Delega via A2A.
- Skills on demand.
- Ottimizzato composito, chiaramente distinto dagli A/B a singola variabile.

Evidenzia in ogni schermata:
dati sintetici, LIVE/REPLAY, modello/deployment, numero di chiamate,
completezza del costo e stato della cattura.

Il diff deve far vedere anche cio che NON cambia fra due esperimenti.

### 17. Conversazione principale per il palco

Implementa uno scenario riproducibile con questi sei turni:

1. "Dov'e il mio ordine ORD-1042?"
2. "Posso restituirlo? Non l'ho usato."
3. "Preciso meglio: la camicia era difettosa al primo utilizzo."
4. "Se la sostituisco, consigliami una camicia simile sotto i 40 dollari."
5. "La camicia costava 29.99 dollari: il rimborso sara di 29.99?"
6. "Confermo: prepara la richiesta di reso per il difetto."

Risultati attesi, definiti dai dati e non iniettati nel modello:

- Consegna verificata con OrdersAgent.
- Ripensamento non ammesso dalla policy outlet dopo 18 giorni.
- Rivalutazione corretta dopo il nuovo fatto sul difetto.
- Consultazione reale di CatalogAgent con vincoli rispettati.
- Correzione dell'importo a 19.99 USD effettivamente pagati.
- Creazione idempotente di una bozza sintetica, non rimborso reale.

La frase del secondo turno e il chiarimento del terzo servono a mostrare
aggiornamento del contesto, non a fissare per sempre la prima interpretazione.

Mostra per ogni turno:
percorso fra agenti, richieste AI, prompt inviati, storico, token, costo
incrementale, costo cumulativo, latenza e valutazione dei fatti.

Prepara anche:

- Uno scenario semplice dove un modello piccolo puo bastare.
- Uno scenario difficile dove cambiare modello puo essere utile.
- Un caso dove il modello piu grande non produce un beneficio giustificabile.
- Un caso dove ridurre lo storico o il contesto fa perdere qualita.

Queste sono categorie da cercare nel dataset, non risultati da falsificare.
Se un effetto non emerge, descrivi il risultato e modifica il disegno
dell'esperimento sul set di sviluppo, non le risposte salvate.

### 18. Matrice degli esperimenti

Implementa esperimenti separati, con hypothesis e changedFactors registrati:

| ID | Confronto | Variabile principale | Evidenza richiesta |
| --- | --- | --- | --- |
| E01 | GPT-5 vs GPT-6; Astra/Sol vs Luna | Modello | Qualita, errori, token, latenza, costo |
| E02 | BAD_BASELINE vs GOOD_GENERAL | Prompt | Errori verificabili, tool usati e verbosita |
| E03 | Matrice 2 x 2 modello/prompt | Interazione modello-prompt | Effetto dell'adattamento, non solo ranking modelli |
| E04 | Full history vs delega strutturata | Contesto di delega | Contenuto trasferito, token ripetuti e qualita |
| E05 | Delega locale vs A2A | Trasporto della delega | Overhead, payload e chiamate AI equivalenti |
| E06 | Tool locale vs MCP | Trasporto tool | Discovery, tool latency, schema e risultato |
| E07 | Istruzioni eager vs skills on demand | Caricamento istruzioni | Token, caricamenti, chiamate aggiuntive e qualita |
| E08 | FullHistory vs CompactHistory | Gestione storico | Costo cumulativo, costo riassunto e perdita di fatti |
| E09 | Risultato tool ampio vs proiezione utile | Volume dei dati | Byte, input token e mantenimento delle evidenze |
| E10 | Prefisso stabile e caching | Riuso osservato della cache | Cached tokens, eventuali cache write e costo netto |
| E11 | Specialisti indipendenti sequenziali/paralleli | Scheduling | Durata critica, consumo totale e failure |

E01-E08 costituiscono il nucleo. E09-E11 sono approfondimenti da preparare
dopo il percorso principale; non devono ritardare la prima demo end-to-end.

Regole essenziali:

- E05 mantiene identici modelli, prompt, contesto, tool e task.
- E06 mantiene equivalenti gli schemi tool visibili al modello e gli output.
- E07 mantiene lo stesso materiale disponibile e lo stesso criterio di successo.
- E04 non viene presentato come prova che un protocollo e piu veloce.
- Una chiamata deterministica a un tool non si confronta con un agente remoto
  dotato di LLM per ricavare l'overhead "MCP contro A2A".
- Se cambia il comportamento del modello, mostra il cambiamento del call graph.
- Non attribuire a un protocollo differenze dovute a token, retry o cache.

Per E05/E06 documenta separatamente eventuali misure di solo trasporto, senza
inferenza: servono a isolare l'overhead del protocollo e non a stimare la
latenza di Azure. Affiancale al benchmark end-to-end con modelli veri.
Un risultato su loopback non e una previsione di rete geografica.

Per caching distingui stato osservato e stato desiderato:
non dichiarare "cold" una run soltanto perche ha una nuova conversationId.
Non assumere di poter svuotare la cache del provider.
Usa cached_tokens e gli altri contatori realmente disponibili.
Verifica parametri e prezzi per la famiglia specifica: non tutte supportano
le stesse opzioni, e le scritture della cache possono avere una tariffa.

### 19. Runner, valutazione e statistica

Il runner deve eseguire scenari multi-turno, creare sessioni isolate,
variare un fattore alla volta e salvare ogni run, comprese quelle fallite.

Prevedi:

- Dry-run senza chiamate Azure.
- Smoke test ristretto.
- Pilot su pochi scenari per stimare budget e variabilita.
- Batch approvato sui confronti scelti, non una combinazione cartesiana illimitata.
- Warm-up separato, registrato e comunque conteggiato nel costo del laboratorio.
- Ordine A/B randomizzato o alternato, con impostazioni e seed del runner salvati.
- Stato delle cache applicative, connessioni, discovery e concorrenza registrato.

Non chiamare riproducibili le risposte del modello in senso deterministico:
sono riproducibili dati, configurazione e procedura di misura.

Usa piu ripetizioni per scenario; proponi inizialmente cinque, dopo il pilot
e previa approvazione del costo. Numero di casi e ripetizioni restano visibili.

Misura almeno:

- Accuratezza di routing e scelta degli specialisti.
- Correttezza dei fatti, delle policy e degli importi.
- Capacita di chiedere chiarimenti.
- Fonti corrette e supporto delle affermazioni.
- Successo del task e assenza di azioni non autorizzate.
- Errori, timeout, retry e risposte incomplete.
- Numero di invocazioni agenti, chiamate LLM, tool e caricamenti skill.
- Input, cached input, output, reasoning e altri meter disponibili.
- Costo per conversazione e costo per task corretto.
- Time-to-first-answer-token visibile all'utente.
- Durata end-to-end, durate degli agenti e dei trasporti.

Non chiamare TTFT il primo evento SSE di avanzamento.
Se disponibile, misura separatamente il primo token del provider.
Per rami paralleli la durata end-to-end non e la somma delle durate dei figli.

Usa prima controlli deterministici su fatti, decisioni e schemi.
Un judge LLM e opzionale, deve usare un modello OpenAI su Azure, avere rubrica
versionata e non vedere il nome della variante candidata quando possibile.
Conserva giudizi e costo separatamente e calibra un campione con revisione umana.
Non scambiare l'autovalutazione dell'agente per qualita oggettiva.

Riporta mediane, dispersione, sample size e percentili quando sensati.
Un p95 su pochissime osservazioni deve essere etichettato come fragile.
Per intervalli di confidenza o confronti aggregati rispetta l'appaiamento dei
casi: turni e ripetizioni dello stesso scenario non sono casi indipendenti.

Conserva anche i fallimenti e le esclusioni, con motivazione.
Per il costo per task corretto includi i tentativi falliti del batch;
se non ci sono successi, il rapporto non e zero.

Prima del holdout definisci una soglia di non-regressione della qualita.
Considera bloccanti errori su autorizzazioni, importi e azioni non confermate.
Non dichiarare "piu efficiente" una variante solo perche spende meno ma sbaglia
di piu. Evidenzia i compromessi qualita/costo/latenza.

### 20. Evidence bundle per costruire le slide dopo

Implementa un export locale redatto contenente:

- manifest.json con schema, run ID e provenienza LIVE/REPLAY.
- Snapshot di configurazione, versioni package e versione applicativa.
- Dataset/scenario IDs e hash, con clock e seed.
- Prompt e skills effettivamente utilizzati, dopo redazione.
- Conversazioni osservabili complete in JSON/JSONL.
- Registro per-call con usage originale, normalizzato e costo.
- Scambi A2A e MCP collegati alle richieste Azure.
- Listino usato con fonte, data, condizioni e hash.
- Risultati grezzi, valutazioni, fallimenti ed esclusioni.
- CSV riassuntivi e grafici esportabili, preferibilmente SVG.
- Un report Markdown breve generato dai risultati.

Ogni affermazione candidata per le slide deve avere:

1. Testo dell'affermazione.
2. Esperimento e variante di riferimento.
3. Numerosita, metrica, formula e risultato.
4. ID delle run che la supportano.
5. Limiti e possibili fattori confondenti.
6. Link relativo al grafico/trace/conversazione.

Genera una "claim ledger", non uno storytelling scollegato dai dati.
Se non esistono misure, riporta "da misurare"; non inserire valori inventati.
Una run scelta per il palco puo essere illustrativa, ma l'aggregato deve
includere anche gli altri casi e non solo i successi.

Grafici desiderati:

- Costo cumulativo lungo i turni.
- Input totale/cache e output per agente e richiesta.
- Latenza end-to-end con waterfall.
- Qualita contro costo, con numero di casi visibile.
- Overhead di trasporto isolato vs latenza end-to-end.
- Istruzioni eager vs skills, includendo il costo del caricamento.

Non produrre grafici "prima/dopo" a partire da metriche di modalita diverse.
Non includere informazioni sensibili nei nomi dei file o negli screenshot.

### 21. Scaletta della sessione: esattamente 60 minuti

Prepara un runbook pratico con questa distribuzione:

| Minuti | Contenuto | Collegamento alle evidenze |
| --- | --- | --- |
| 0-5 | Problema e architettura dei quattro agenti | Confini del sistema e definizioni |
| 5-12 | Conversazione iniziale del chatbot | Scenario ORD-1042 |
| 12-22 | Trace, token, costo e correzione di un prompt | E02 e inspector della conversazione |
| 22-32 | Cambio modello e prompt specifici | E01 ed E03 |
| 32-44 | MCP, A2A e skills senza confronti ingannevoli | E05, E06, E07; due dimostrazioni brevi |
| 44-51 | Storico, contesto di delega, payload e cache | E04, E08; approfondimenti E09/E10 se pronti |
| 51-55 | Risultati, compromessi e checklist pratica | Claim ledger e grafici aggregati |
| 55-60 | Domande | Cinque minuti riservati |

Non eseguire l'intera campagna di benchmark sul palco.
Scegli tre o quattro passaggi LIVE brevi e prepara le altre evidenze prima.
Per ogni segmento del runbook indica:

- Domanda da fare al pubblico e ipotesi da verificare.
- Scenario/preset esatto e interazione da eseguire.
- Pannello da aprire e dato da osservare.
- Risultato misurato da raccontare, quando disponibile.
- Spiegazione alternativa se il comportamento LIVE cambia.
- Run REPLAY di riserva, solo se registrata realmente.

Se la rete manca, mostra il badge REPLAY e dillo esplicitamente.
Non preparare ancora 40 slide teoriche: prima servono dati ed evidenze.
Proponi successivamente una presentazione di circa 15-20 slide, subordinata
alla selezione dei risultati realmente ottenuti.

### 22. Test e criteri di accettazione

Usa strumenti e test coerenti con lo stack; evita nuove dipendenze inutili.
Prevedi unit test .NET, integration test e test browser mirati.

Test obbligatori:

- Seed riproducibile, integrita di ordini/prodotti e policy.
- Clock controllato e precedenza outlet/difetto.
- Isolamento fra clienti e conversazioni.
- Uso effettivo dei quattro agenti nel caso principale.
- Equivalenza dei servizi di dominio fra tool diretti e MCP.
- Delega agli stessi specialisti localmente e via A2A.
- Lazy loading skill, percorsi autorizzati e versionamento.
- Persistenza dello storico e rispetto delle correzioni multi-turno.
- Preservazione degli item/tool call richiesti dall'API scelta.
- Streaming, cancellazione e riconnessione senza doppia spesa applicativa.
- Bozza di reso idempotente con conferma.
- Cattura del body serializzato e correlazione di tutte le chiamate.
- Redazione di segreti e indicazione esplicita delle catture incomplete.
- Calcolo costo, cached input e reasoning senza doppio conteggio.
- Tariffe aggiuntive, context tier, rate mancanti e usage sconosciuto.
- Distinzione fra duplicati di telemetria e veri retry.
- Statistiche con denominatori corretti, failure incluse e divisione per zero.
- Export ricaricabile e ricostruzione della run dopo riavvio.
- Nessun fallback automatico a una modalita alternativa.

I test contabili possono usare tariffe e usage sintetici chiaramente marcati
TEST_ONLY. Non esportarli come prezzi o misurazioni del servizio.

Esegui build/type-check e i test pertinenti; correggi e ripeti fino al successo.
I test con fixture non dimostrano che SDK, modelli e protocolli funzionino in
LIVE: serve uno smoke test autorizzato su Azure e un percorso A2A/MCP reale.

Il primo incremento richiesto usa Azure OpenAI LIVE. Deve partire da Aspire e
consentire di esercitare tutte e tre le demo con configurazione valida.
Il report distingue funzioni implementate da verifiche LIVE ancora bloccate
per mancanza di deployment o budget. Non dichiarare benchmark reali completati.

Prima di presentarla come demo LIVE completa, verifica anche questi criteri:

1. Parte dall'AppHost Aspire su Windows/PowerShell, in locale e in container
   quando il motore Docker e disponibile.
2. La chat funziona in LIVE con i quattro agenti.
3. Lo scenario multi-turno e ispezionabile fino alle richieste Azure.
4. Token e costi sono riconciliabili con usage e listino, con limiti dichiarati.
5. Funzionano i tre assi locale/A2A, diretto/MCP, eager/skills.
6. I confronti E01-E08 sono eseguibili senza modificare codice.
7. Esiste almeno un evidence bundle proveniente da run LIVE autorizzate.
8. Il piano B REPLAY riproduce una registrazione reale.
9. Errori e dati mancanti non vengono mascherati.
10. Il runbook copre 60 minuti, comprese le domande.

La qualita richiesta per i confronti va verificata sul dataset: il fatto che
una variante chiamata "ottimizzata" non vinca non e un bug da nascondere.
Implementazione completa e ipotesi sperimentale confermata sono cose diverse.

### 23. Sequenza di implementazione

Lavora per incrementi verificabili, senza saltare direttamente a una UI finta.

**Fase 1 - Vertical slice**

Dataset, quattro agenti, chat React, sessioni, singolo provider Azure,
telemetria per-call, inspector e motore di stima costo.
AppHost Aspire e le tre risorse demo sono parte di questa prima fase.
Importa e congela il catalogo pubblico, con immagini esclusivamente nella UI.
Verifica lo scenario principale LIVE previa autorizzazione.

**Fase 2 - Laboratorio**

Prompt lab, registro modelli, storico, A2A, MCP, skills e configurazioni
immutabili. Verifica ogni integrazione e la comparabilita dei percorsi.

**Fase 3 - Evidenze**

Runner, valutazione, budget, report, export, registrazioni e runbook.
Esegui solo i batch approvati. Non avviare tutte le combinazioni automaticamente.

In ogni fase:

- Parti da esempi ufficiali compatibili e da una prova piccola.
- Implementa davvero il percorso principale.
- Testa, osserva gli errori e correggi la causa.
- Aggiorna la documentazione direttamente correlata.
- Mantieni visibili limitazioni e funzionalita ancora non verificate.

Se una dipendenza o capacita manca, cerca un'alternativa supportata.
Non sostituire il requisito con una simulazione non dichiarata.
Se servono credenziali, consenso di spesa o una decisione sostanziale, fermati
su quel punto e richiedi l'informazione; continua le parti locali indipendenti.

### 24. Consegna e riepilogo finale richiesto al coding agent

Consegna codice funzionante, configurazione senza segreti, dataset, prompt,
skills, test, documentazione di avvio, runner ed export.

Nel riepilogo finale indica:

- Cosa e stato implementato e quali versioni sono state testate.
- Come avviare e arrestare ogni componente.
- Come selezionare deployment e configurare credenziali in modo sicuro.
- Come eseguire lo scenario principale e i confronti.
- Test effettivamente eseguiti ed esito.
- Quali misure provengono da LIVE e quali dati sono replay di run precedenti.
- Costo noto del laboratorio e componenti eventualmente non quantificabili.
- Posizione delle evidenze utili per costruire le slide.
- Limitazioni e verifiche ancora necessarie.

Non dichiarare pronta una demo che ha soltanto UI e risposte non verificate.
Non chiamare "vero caso di produzione" uno scenario sintetico:
e un caso realistico con inferenza reale, quando eseguito in LIVE.

## Fonti ufficiali iniziali da verificare durante l'implementazione

Le fonti seguenti sono state consultate per preparare la specifica.
Le API, i package, le disponibilita regionali e i listini possono cambiare.
Riverifica quelli effettivamente adottati; non usare esempi di un'altra
famiglia come prova di compatibilita.

### Agent Framework .NET, interoperabilita e osservabilita

- [Azure OpenAI con Agent Framework](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/azure-openai)
  - Agenti .NET locali con deployment Azure, senza obbligo di Foundry Agent Service.
- [Orchestrazione Handoff](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/handoff)
  - Distinzione fra trasferimento del controllo e agent-as-tools/supervisione.
- [Client A2A in Agent Framework](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/a2a)
  - Discovery, binding e uso di agenti remoti.
- [Migrazione A2A SDK v1](https://learn.microsoft.com/en-us/agent-framework/migration-guide/agent-to-agent-sdk-v1)
  - Hosting e API aggiornate da allineare ai package effettivamente scelti.
- [Hosting A2A](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/a2a/server)
  - Tutorial di hosting; verificare le differenze rispetto alla guida di migrazione.
- [Trasporti del C# SDK ufficiale MCP](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)
  - Streamable HTTP client/server. Il ramo main non certifica la disponibilita
    di ogni funzione in una particolare versione NuGet.
- [Agent Skills](https://learn.microsoft.com/en-us/agent-framework/agents/skills)
  - Provider .NET nativo e caricamento progressivo di istruzioni e risorse.
- [Osservabilita Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/observability)
  - OpenTelemetry e rischio di duplicare contenuti ai diversi livelli.
- [Chat-level middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/chat-middleware)
  - Intercettazione logica delle chiamate; non equivale al body HTTP definitivo.
- [JavaScript e React nell'AppHost Aspire](https://aspire.dev/integrations/frameworks/javascript/)
  - Orchestrazione locale, service discovery e pubblicazione dei frontend.
- [DummyJSON Products API](https://dummyjson.com/docs/products)
  - Catalogo pubblico di esempio, proiezione dei campi e immagini per la UI.

### Azure: servizio, stato, token e costo

- [Azure OpenAI Responses API](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses)
  - API, deployment supportati, conversazioni e stato.
- [Azure OpenAI reasoning models](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)
  - Parametri e limiti per modello; reasoning tokens e consumo.
- [Prompt caching su Azure OpenAI](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/prompt-caching)
  - Cache read/write, requisiti e differenze fra famiglie.
- [Prezzi Azure OpenAI](https://azure.microsoft.com/en-us/pricing/details/azure-openai/)
  - Fonte del listino Azure, da contestualizzare per deployment.
- [Dati, privacy e sicurezza dei modelli venduti da Azure](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy)
  - Confine tra servizio Azure e fornitore del modello.

### Guide OpenAI: comportamento e prompting, non listino Azure

- [Reasoning best practices](https://developers.openai.com/api/docs/guides/reasoning-best-practices)
  - Indicazioni sui modelli reasoning; controllare l'applicabilita alla versione.
- [GPT-4.1 prompting guide](https://developers.openai.com/cookbook/examples/gpt4-1_prompting_guide)
  - Esempio di guida specifica per instruction following.
- [GPT-5 prompting guide](https://developers.openai.com/cookbook/examples/gpt-5/gpt-5_prompting_guide)
  - Esempio di guida specifica, non una regola universale per ogni GPT successivo.
- [Conversation state](https://developers.openai.com/api/docs/guides/conversation-state)
  - Stato esplicito, item di risposta e continuita; verificare il supporto Azure.

## Fine del prompt
