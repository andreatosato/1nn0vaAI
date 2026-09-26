# Osservare l'AI per migliorare la qualità delle risposte e abbassare il costo dei modelli

## PPT attuale: 27 slide, 60 minuti

La versione aggiornata è
[Osservare-AI-Andrea-Tosato-senza-limiti.pptx](./Osservare-AI-Andrea-Tosato-senza-limiti.pptx),
rigenerabile con `node presentazione\genera-presentazione.cjs` dalla root.
Le durate nelle note del relatore determinano automaticamente i footer.

| Minuti | Slide | Contenuto |
|---|---|---|
| 00–05 | 1–3 | Apertura, metriche e mappa dei workflow |
| 05–13 | 4–8 | Rubrica, osservabilità, architettura, tool e descrizioni |
| 13–16 | 9–11 | Diagrammi A2A vs Skill dalla conversazione condivisa |
| 16–18 | 12–13 | Skill distribuite, agent-as-tool, dialogo e responsabilità |
| 18–26 | 14–16 | Evoluzione dei prompt, famiglie di modelli e contratto |
| 26–40 | 17–21 | Token, cache, costo, latenza e disegno dell'esperimento |
| 40–47 | 22 | Demo guidata |
| 47–54 | 23–25 | Sei run LIVE, prompt effettivi ed esiti |
| 54–55 | 26 | Pratiche operative |
| 55–60 | 27 | Chiusura e Q&A |

### Sei slide aggiunte

- **Slide 3:** tutte le voci di *Workflow capabilities*, separate in
  composizione, interazione/durabilità, operatività e cinque orchestrazioni
  (Sequential, Concurrent, Handoff, Group Chat, Magentic). Agent-as-tool è
  un pattern aggiuntivo, non un sesto workflow built-in.
- **Slide 9:** confronto grafico dei due flussi: A2A con secondo ciclo di
  reasoning e Distributed Skill con reasoning nel Main Agent.
- **Slide 10:** trasformazione grafica da quattro specialist agent a quattro
  skill collegate ai rispettivi MCP tools, mantenendo distribuiti i servizi.
- **Slide 11:** diagramma conclusivo “delego il reasoning” contro “delego la
  capability”, con contesto/stato isolati da un lato e istruzioni/tool/servizio
  remoto dall'altro.
- **Slide 12:** differenze tra tool, skill e agent-as-tool, con attribuzione
  a Tommaso Stocchi, foto del profilo e collegamenti cliccabili.
- **Slide 13:** delega con ritorno, handoff e collaborazione; distinzione
  tra pattern e protocolli A2A/MCP. Agent-as-tool è adatto a sottotask e
  responsabilità chiare, ma non garantisce una latenza inferiore.

Le tre coppie di misure citate nella slide 13 appartengono all'articolo,
**non ad AI Observatory**: le skill risultano più rapide in quelle prove,
ma con circa il 22% di token in più. Non è un benchmark controllato né
un confronto di costi in dollari. Le sei run della nostra demo restano
inalterate nelle slide 23–25. La nostra implementazione Skills usa
API HTTP dedicate e tool nel router, non MCP.

### Fonti delle nuove slide

- [Ragionamento ChatGPT condiviso](https://chatgpt.com/s/t_6ab6cd6b438481919846351e496ee155)
- [Tommaso Stocchi: From Specialist Agents to Distributed Skills over MCP](https://devblogs.microsoft.com/agent-framework/from-specialist-agents-to-distributed-skills-over-mcp/),
  Microsoft Developer Blogs, 16 settembre 2026.
- [Profilo autore e fonte della foto](https://devblogs.microsoft.com/agent-framework/author/tstocchi/).
  [Foto originale 150×150](https://devblogs.microsoft.com/agent-framework/wp-content/uploads/sites/78/2024/09/codemotion25-small-150x150.webp),
  convertita in [PNG per il PPT](./assets/tommaso-stocchi.png), senza generare
  un ritratto artificiale. La disponibilità pubblica non implica una licenza libera.
- [Workflow capabilities](https://learn.microsoft.com/en-us/agent-framework/workflows/)
- [Handoff e agent-as-tools](https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/handoff#differences-between-handoff-and-agent-as-tools)

Le note contengono fonti, precisazioni e limiti delle misure. Non è stato
generato un PDF.

## Proposta iniziale di una sessione di 60 minuti (storico)

La proposta qui sotto precede il PPT attuale: numerazione, tempi e budget
non sono quelli della versione senza limiti. Per presentare usare la tabella
aggiornata sopra e le note del PPT.

**Sottotitolo:** Dal testo della chat alle evidenze: qualità, latenza, token e costo.

### Abstract proposto

Partiamo da un assistente conversazionale per l'e-commerce e seguiamo una
richiesta lungo il suo percorso: risposta, strumenti, servizi, chiamate al
modello e telemetria. Con la demo confrontiamo architetture, prompt e modelli,
osservando tempi, token e costi registrati per run. Il punto non è dichiarare
in anticipo quale modello sia migliore: è imparare a raccogliere prove
ripetibili, individuare sprechi e verificare che ogni ottimizzazione preservi
la correttezza della risposta.

**Trasparenza sul caso:** lo scenario è realistico, ma gli ordini e le policy
sono sintetici e il catalogo è uno snapshot pubblico DummyJSON. La demo non
contiene dati o misure di un cliente di produzione. Presentarla come laboratorio
end-to-end, senza attribuirle risultati di produzione.

## Risultati per il pubblico

Alla fine della sessione chi partecipa saprà:

1. distinguere la qualità percepita da correttezza, grounding e rispetto delle
   policy;
2. seguire una risposta dal Router alle API o agli agenti delegati;
3. leggere trace, chiamate, token, cache, latenza e costo distinguendo
   misure disponibili e dati assenti;
4. impostare un confronto controllato che cambi una sola variabile per volta;
5. scegliere un intervento sul prompt, sul contesto, sui tool o sul modello e
   verificarne l'effetto sulla qualità oltre che sul costo.

## Scaletta: 60 minuti, domande incluse

| Minuti | Durata | Parte | Punto chiave |
|---|---:|---|---|
| 00–04 | 4' | Apertura | Una buona risposta può nascondere un percorso costoso o scorretto. |
| 04–10 | 6' | Che cosa significa qualità | Fatti, policy, correzioni, fonti e azioni autorizzate sono verificabili. |
| 10–17 | 7' | Architettura e telemetria | Inline, Skills e A2A cambiano il percorso; osservare rende visibile la differenza. |
| 17–32 | 15' | Demo guidata | Ordine e reso, catalogo, traccia e ledger LIVE. |
| 32–42 | 10' | Esperimento sui prompt | BAD, GOOD e ridondanza controllata; isolare una variabile. |
| 42–50 | 8' | Token, latenza e costo | Interpretare i numeri per chiamata e per run, non come fattura o classifica. |
| 50–56 | 6' | Consigli pratici e limiti | Ridurre il costo senza sacrificare correttezza, sicurezza o misurabilità. |
| 56–60 | 4' | Domande e chiusura | Una domanda, una misura, un'ipotesi da testare. |
| **Totale** | **60'** |  | **Comprende Q&A.** |

## Svolgimento e note per chi presenta

### 00–04 · Apertura

**Slide 1 — Titolo e promessa**

- Presentare la domanda: «Come facciamo a sapere se una risposta migliore ci
  costa davvero di più? E come dimostriamo il contrario?»
- Esplicitare che la sessione mostrerà un metodo di osservazione e confronto,
  non una classifica preconfezionata.
- Anticipare i quattro segnali che seguiranno: correttezza, latenza, token e
  costo.

**Transizione:** una risposta fluida non è ancora una risposta corretta.

### 04–10 · Che cosa misuriamo come qualità

**Slide 2 — La qualità si scompone in controlli**

Presentare la conversazione ORD-1042 come esempio di assistenza:

- usare l'importo pagato, distinguendolo dal prezzo di listino;
- distinguere il ripensamento outlet dalla segnalazione di un difetto;
- aggiornare la valutazione quando il cliente corregge il motivo;
- proporre prodotti che esistono davvero nel catalogo;
- non preparare una bozza senza consenso esplicito.

Chiedere al pubblico quale errore sarebbe più grave: un tono poco elegante,
un prezzo inventato o un reso autorizzato per il motivo sbagliato?

**Slide 3 — Dal giudizio all'evidenza**

- Definire una rubrica: fatti esatti, policy applicata, uso del contesto,
  grounding nel catalogo, chiarezza e rispetto del consenso.
- Separare una valutazione qualitativa dalla telemetria quantitativa.
- Spiegare che token e latenza non misurano da soli la qualità.

### 10–17 · Architettura e osservabilità

**Slide 4 — Tre percorsi, stessi servizi**

- **Inline:** un Router con istruzioni inline interroga i servizi business HTTP.
- **Skills:** un Router usa istruzioni caricate come skill e interroga gli
  stessi servizi HTTP.
- **A2A:** il Router delega via protocollo agli specialisti remoti.
- I servizi Catalog, Orders e Returns e il dataset restano distinti dagli
  agenti che invocano il modello.

**Slide 5 — Che cosa rende visibile la demo**

Mostrare rapidamente il percorso: chat → Router → tool/protocollo → servizio →
risposta; quindi la traccia, l'Inspector e il ledger per chiamata. Precisare
che una delega o un retry può cambiare il numero di inferenze: il confronto tra
architetture non isola automaticamente il solo overhead di rete.

### 17–32 · Demo guidata

**Slide 6 — Il caso e i dati**

1. Aprire **Dati della demo** e mostrare ORD-1042: consegna, stato outlet,
   listino **29,99 USD** e importo pagato **19,99 USD**.
2. Dichiarare la provenienza: catalogo pubblico congelato; ordini e policy
   sintetici.
3. Passare a **Confronti guidati** e mostrare le impostazioni complete e lo
   scenario ricevuto dal backend.

**Percorso nella UI**

1. Nella chat chiedere: `Dov'è il mio ordine ORD-1042?`
2. Proseguire con: `Posso restituirlo? Non l'ho usato.`
3. Correggere il contesto: `Preciso meglio: la camicia era difettosa al primo utilizzo.`
4. Chiedere un prodotto concreto: `Consigliami una camicia sotto i 40 dollari.`
5. Aprire **Traccia** e **Inspector** per seguire l'accesso al servizio e le
   chiamate effettive.
6. Aprire **Token e costi** per vedere conteggi provider, costo registrato,
   provenienza e copertura per chiamata.

Non è necessario inviare tutti i turni durante la sessione. Scegliere in base
al tempo e usare le evidenze salvate per gli altri passaggi. L'ultimo turno che
crea una bozza sintetica richiede consenso separato: si può omettere.

**Confronto GPT-5 / GPT-6 Sol**

- Usare il terzo preset con la stessa domanda, prompt GOOD, architettura e
  limiti; creare una nuova chat per ogni modello.
- Mostrare una coppia di run con configurazioni corrispondenti. Se non è stata
  raccolta prima, eseguire solo le due richieste strettamente necessarie e
  approvarle manualmente.
- Leggere qualità, chiamate, token, latenza e costo; non dedurre un vincitore
  da una sola risposta.

Ogni turno LIVE richiede consenso manuale e porta un budget approvato di
**0,10 USD per run**. Il budget della demo è un limite applicativo per run,
non un tetto di fatturazione imposto dal provider.

### 32–42 · Esperimento sui prompt

**Slide 7 — BAD, GOOD, GOOD con sola ridondanza**

- Aprire il preset prompt e confrontare le tre configurazioni.
- Spiegare il disegno: BAD → GOOD cambia il profilo; GOOD → GOOD ridondante
  cambia soltanto il blocco ridondanza.
- Per ogni variante usare una nuova chat e ripetere gli stessi due messaggi
  dello scenario di correzione.
- Valutare la decisione sul reso e la gestione della correzione, non la
  lunghezza o la sicurezza retorica della risposta.

Se il tempo non basta, mostrare le configurazioni e il metodo senza inviare
tutte le varianti. Non inventare output mancanti: raccogliere i run prima
dell'evento oppure dichiarare la verifica come esercizio per il pubblico.

### 42–50 · Interpretare token, latenza e costo

**Slide 8 — Leggere il ledger senza confondere le metriche**

- Distinguere input, input letto da cache, input scritto in cache, output e
  reasoning; non sommare nuovamente cache o reasoning se sono già inclusi nei
  totali.
- Distinguere costo del singolo run da costo dell'intera conversazione.
- Osservare il numero di chiamate e gli agenti realmente invocati, non quelli
  soltanto disponibili.
- Verificare provenienza e completezza: un valore assente resta non
  disponibile.
- Il costo registrato è una stima calcolata con usage e pricing disponibili,
  non la fattura Azure.

**Tabella da compilare con run realmente osservati**

| Variante | Correttezza / grounding | Chiamate modello | Input / output | Latenza | Costo registrato |
|---|---|---:|---:|---:|---:|
| GPT-5 / GOOD | Da valutare | Da misurare | Da misurare | Da misurare | Da misurare |
| GPT-6 Sol / GOOD | Da valutare | Da misurare | Da misurare | Da misurare | Da misurare |

Compilare con gli stessi messaggi, configurazione documentata e run selezionati.
Non inserire in anticipo numeri d'esempio come se fossero il risultato del
confronto.

### 50–56 · Consigli pratici e limiti

**Slide 9 — Una checklist prima di ottimizzare**

1. Ridurre il contesto inviato solo dopo aver verificato che i fatti necessari
   restino disponibili.
2. Usare tool mirati per recuperare dati invece di copiare l'intero catalogo
   nel prompt.
3. Rendere istruzioni e formato della risposta chiari; misurare separatamente
   una variante prolissa.
4. Limitare le chiamate e scegliere il modello adeguato solo dopo aver
   verificato capability, qualità e costo sullo stesso caso.
5. Ripetere le prove, alternare l'ordine dei modelli e conservare prompt,
   scenario, dati e run per la riproducibilità.
6. Mantenere consenso, privacy e verifiche di policy anche quando si ottimizza
   latenza o costo.

I risultati dipendono da cache, variabilità del provider, versione del modello
e numero di ripetizioni. Non promettere risparmi percentuali senza una
misurazione comparabile.

### 56–60 · Domande e chiusura

**Slide 10 — Dal grafico alla prossima ipotesi**

- Rispondere alle domande e chiedere quale costo o errore il pubblico
  misurerebbe per primo nel proprio sistema.
- Chiudere con il ciclo: **osserva → formula un'ipotesi → cambia una variabile
  → verifica qualità e costo → conserva l'evidenza**.

## Preparazione dello speaker

### Prima dell'evento

- Avviare Aspire dalla root del progetto:
  `dotnet run --project .\src\Observatory.AppHost --launch-profile http`
- Verificare in anticipo UI, API e `liveReady` per GPT-5 e GPT-6 Sol.
- Aprire **Confronti guidati**, **Dati della demo**, **Traccia** e **Token e
  costi** nelle schede necessarie.
- Raccogliere almeno tre run per variante del confronto che si vuole
  presentare come risultato; fissare messaggi, prompt, architettura e dati.
- Registrare configurazione e provenienza di ogni misura. Preparare una
  copia delle schermate o dei run già persistiti per il fallback.
- Provare il percorso LIVE con consenso manuale e budget per run; controllare
  che non vi siano conversazioni precedenti nella chat da dimostrazione.
- Predisporre schermate e run LIVE già persistiti come piano alternativo se il
  servizio esterno non è disponibile; indicare chiaramente che non si tratta
  di una nuova misurazione.

### Durante la demo

- Preparare un preset non invia richieste; per ogni inferenza LIVE occorre
  premere **Invia** e autorizzare esplicitamente quel turno.
- Controllare che la chat sia nuova quando si cambia variante.
- Non eseguire azioni sintetiche senza consenso. Non presentare una bozza come
  pagamento o reso reale.
- Se la rete rallenta, non ripetere alla cieca un invio dall'esito incerto:
  controllare lo storico e la traccia.
- Se manca usage o pricing, mostrarlo come non disponibile; non stimare a
  occhio né riutilizzare una prova precedente come nuova evidenza.

## Riferimenti della demo

- [Tre confronti preimpostati](../ESEMPI-PREIMPOSTATI.md)
- [Domande e scenari](../DOMANDE-DEMO.md)
- [Architettura delle tre demo](../ARCHITETTURA.md)
- [README del progetto](../README.md)
