# Tre confronti preimpostati

In ogni demo apri **Confronti guidati** (`/#/{tech}/examples`).
I preset partono da **LIVE**, usando i profili Foundry configurati nel backend.
Un modello assente, non configurato, non autorizzato o senza tariffe LIVE blocca la variante:
non viene sostituito con un altro modello.
GPT-5 e GPT-6 Sol sono stati verificati in LIVE; la disponibilita effettiva
di ogni variante e dichiarata da `liveReady` nel backend, non dal solo deployment.

**Prepara** imposta tutti i campi, elimina gli override, apre il bot con
lo scenario DEVELOPMENT corrispondente e inserisce il primo messaggio.
Non crea conversazioni, non invia e non concede consensi.
Il testo effettivo della guida arriva da `/scenarios`, non da copie nella UI.

## Regole comuni e configurazione completa

| Campo | Valore in tutte le prove, salvo la variante dichiarata |
|---|---|
| Modalita | **LIVE**; consenso manuale a ogni invio |
| Modello | GPT-5 (`gpt5`), salvo il confronto modelli |
| Override per agente | Nessuno: `{}`; in A2A tutti ereditano il modello base |
| Profilo prompt | `good`, salvo la variante BAD |
| Blocchi prompt | `checklist=false`, `outputContract=false`, `examples=false`, `redundancy=false`, `conflictingStyle=false`; solo la variante prolissa attiva `redundancy` |
| Memoria | `full` |
| Trasporto tool | `direct` |
| Max output | 1500 token per chiamata; non obbliga a produrre 1500 token |
| Max chiamate modello | 24 per turno |
| Budget LIVE | **0.10 USD per turno**, autorizzato soltanto con il consenso manuale all'invio; nessuna spesa alla preparazione |
| Consenso bozza | `confirmAction=false`; solo eventuale ultimo turno del caso 1 richiede una scelta manuale |
| Conversazione | **Nuova chat per ogni variante/architettura**; stessa chat fra i turni della singola prova |
| Dati | Stesso snapshot e orologio applicativo congelato al 2026-09-23 |

Il limite di output puo includere token di reasoning secondo il modello:
un risultato troncato va registrato come tale, non come prova di scarsa qualita.
I limiti sono uguali per un confronto a budget di chiamata controllato.
Non vengono inventati parametri di temperatura o reasoning non esposti dall'app.

Se nella chat ci sono gia run o messaggi, i pulsanti **Prepara** sono bloccati.
Apri il bot, premi **Nuova chat**, poi applica il preset. Lo storico precedente
rimane consultabile. Cambiare pagina non azzera le impostazioni.

## 1. Confronto architetture: tutti i domini

**Variante:** Baseline GPT-5 / GOOD. Ripetila in **Inline**, **Skills**, **A2A**.
L'unica variabile e l'architettura. Usa i valori comuni della tabella.

Scenario: `main-six-turns`. Sei messaggi, in ordine, aspettando ogni risposta:

1. `Dov'e il mio ordine ORD-1042?`
2. `Posso restituirlo? Non l'ho usato.`
3. `Preciso meglio: la camicia era difettosa al primo utilizzo.`
4. `Se la sostituisco, consigliami una camicia simile sotto i 40 dollari.`
5. `La camicia costava 29.99 dollari: il rimborso sara di 29.99?`
6. `Confermo: prepara la richiesta di reso per il difetto.`

**Attese, non risultati misurati:**

- Consegna il **2026-09-05**.
- Ripensamento outlet negato: **18 giorni**, limite **14**.
- Dopo il chiarimento, difetto ammesso entro **60 giorni**.
- Alternative Catalog reali sotto **40 USD**, con ID e prezzi.
- Importo pagato **19.99 USD**, distinto dal listino **29.99 USD**.
- Ultimo turno senza consenso: richiesta di conferma, **nessuna bozza**.
  Se vuoi anche la bozza sintetica, seleziona manualmente il consenso in tutte
  le architetture. Nessun rimborso reale. Le bozze sono idempotenti: non
  confrontare una prima creazione con un recupero gia esistente come se fossero
  lo stesso stato iniziale.

Per una prova interamente in sola lettura lascia il consenso bozza spento.
In A2A osserva Router e deleghe agli specialisti; Inline e Skills hanno un
solo agente modello Router e tre servizi HTTP. Non imporre che ogni singolo
turno attivi tutti gli agenti: valuta l'intera sequenza.

## 2. Confronto prompt: ambiguo, chiaro, prolisso

Mantieni **la stessa tecnologia** per tutte le varianti, preferibilmente Inline
per iniziare, e **GPT-5**. Nuova chat per ciascuna:

| Variante | Profilo prompt | Blocchi |
|---|---|---|
| A | `bad` | Tutti spenti |
| B | `good` | Tutti spenti |
| C | `good` | Solo `redundancy=true` |

Confronta **A/B** per il profilo prompt e **B/C** per la prolissita.
Non trattare A/C come confronto a una sola variabile.

Scenario: `correction`. In ogni variante invia:

1. `Voglio restituire ORD-1042 per ripensamento.`
2. `Correggo: era difettoso, non e semplice ripensamento.`

**Attese:** prima negazione per il limite di 14 giorni; poi rivalutazione
del difetto entro 60 giorni, conservando ORD-1042 dal contesto.
Osserva correttezza, chiarezza, ripetizioni e consumo di token. BAD non
garantisce errori e la ridondanza non garantisce peggioramenti: servono misure.
Le autorizzazioni backend restano identiche con qualsiasi prompt.

## 3. Confronto modelli: raccomandazione dal catalogo

Mantieni **la stessa tecnologia**, **GOOD**, tutti i blocchi spenti e i valori
comuni. Nuova chat per ciascuna variante:

| Variante | Profilo modello | Override |
|---|---|---|
| A | GPT-5 (`gpt5`) | Nessuno |
| B | GPT-6 Sol (`gpt6-sol`) | Nessuno |

Scenario: `catalog-shirts`. Stesso messaggio in entrambe:

> Consigliami una camicia sotto i 40 dollari.

**Attese:** prodotti realmente presenti, pertinenti, sotto 40 USD, con ID e
prezzi verificabili in **Dati della demo**. Non fissare una classifica attesa
inventata: controlla la qualita delle raccomandazioni e le fonti.
Verifica l'uso di Catalog nella traccia; confronta token, costo e durata.

## Come registrare un confronto corretto

1. Ripeti almeno **tre volte**, alternando l'ordine delle varianti. Non basta
   una singola risposta per proclamare un modello o un'architettura migliore.
2. In **Inspector** verifica configurazione registrata, prompt, history,
   modello/deployment effettivo e richieste. Una configurazione desiderata
   non prova che il run sia stato eseguito con successo.
3. In **Traccia** controlla servizi/agenti realmente chiamati, errori e retry.
4. In **Token e costi** confronta input, output, cache, reasoning e costo
   stimato; cache e reasoning non si sommano di nuovo al totale input+output.
   Per A2A considera tutte le chiamate degli agenti.
5. La pagina riguarda **il run selezionato**. Per sei turni registra tutti e
   sei i run e somma i costi, non usare solo l'ultimo. Un dato mancante
   non vale zero. Confronta tariffe/versioni e annota la cache.
6. Esporta i run dallo **Storico**. Il replay legge prove precedenti:
   non e una nuova esecuzione e non produce una nuova misura.

| Prova / variante / ripetizione | Tecnologia | Run ID (tutti i turni) | Fatti corretti | Input / output | Costo USD stimato | Durata | Errori, cache, note |
|---|---|---|---|---|---|---|---|
| Da compilare dopo l'esecuzione | | | | | | | |

I costi mostrati sono stime del ledger con tariffe configurate, non la
fattura Azure.

Altre domande e dettagli del dominio: [DOMANDE-DEMO.md](DOMANDE-DEMO.md).
