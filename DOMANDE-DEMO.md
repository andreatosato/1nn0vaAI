# Domande per la demo: Router, Catalog, Orders e Returns

Per tre prove con configurazione completa e varianti a una sola variabile,
usa **Confronti guidati** nella UI e [ESEMPI-PREIMPOSTATI.md](ESEMPI-PREIMPOSTATI.md).
Questi preset eseguono solo LIVE, con consenso manuale a ogni invio.

## Uso senza esecuzioni automatiche

Apri una delle demo **Inline**, **Skills** o **A2A**, poi la chat e il
**Percorso guidato · sei turni**. Il selettore **Scenario DEVELOPMENT** espone gli
scenari di sviluppo restituiti da `/scenarios`, con `main-six-turns` come default.
I casi **holdout sono esclusi**: non usarli per insegnare, modificare o scegliere
i prompt.

- Cambiare scenario non cancella storico o bozza e non crea una conversazione.
- Cliccare una domanda sostituisce soltanto la bozza, azzera i consensi e **non invia**.
- Invia ogni messaggio manualmente e aspetta la risposta prima del successivo.
- I domini e gli output qui sotto e nella guida sono **aspettative**, non risultati
  misurati. Dopo ogni invio confronta risposta, fonti, **Traccia** e **Inspector**.
- **LIVE** richiede configurazione disponibile e consenso manuale per ciascun invio;
  può comportare costi. Questa guida non abilita LIVE e non avvia run.

## Sequenza principale: sei turni nella stessa chat

Usa **Nuova chat** prima della sequenza per evitare fatti ereditati da altre prove.
Le domande riportano il testo di `ScenarioCatalog.cs`: non modificano le fixture.

| # | Domanda da inviare | Domini attesi, coordinati dal Router | Output da verificare (non misurato) |
|---|---|---|---|
| 1 | `Dov'e il mio ordine ORD-1042?` | Orders | Ordine autorizzato ORD-1042, consegnato il **2026-09-05**. |
| 2 | `Posso restituirlo? Non l'ho usato.` | Orders + Returns | Recupero dell'ordine dal contesto; ripensamento outlet **negato**: trascorsi **18 giorni**, limite **14**. |
| 3 | `Preciso meglio: la camicia era difettosa al primo utilizzo.` | Orders + Returns | Il chiarimento cambia il motivo: difetto **ammesso entro 60 giorni**, anche su outlet. |
| 4 | `Se la sostituisco, consigliami una camicia simile sotto i 40 dollari.` | Catalog | Alternative reali del catalogo entro il budget, ID e prezzi in **USD**. Non fissare una classifica inventata. |
| 5 | `La camicia costava 29.99 dollari: il rimborso sara di 29.99?` | Orders + Returns | Proposta massima **19.99 USD effettivamente pagati**, non il listino 29.99; il motivo difetto resta nel contesto. |
| 6 | `Confermo: prepara la richiesta di reso per il difetto.` | Orders + Returns | Con consenso manuale e validazione backend: bozza sintetica **RET-…**, non pagamento. Senza consenso: richiesta di conferma, nessuna bozza. |

Il Router interviene in tutti i turni: questa sequenza esercita i tre domini
business **nell'insieme**, non promette tutti gli specialisti in ogni turno.
Al turno 6 seleziona volontariamente **Autorizzo solo bozza sintetica** se vuoi
crearla; la parola «Confermo» nel testo non basta. Ripetere la stessa richiesta
confermata restituisce la bozza già esistente (idempotenza), non un rimborso reale.

### Quali agenti dovresti vedere?

- **A2A:** un Router e tre agenti specialisti distinti, **Catalog**, **Orders**,
  **Returns**, invocati via deleghe A2A quando necessari. Per il reso il Router
  consulta Orders e Returns; dopo la verifica e il consenso può tornare a Orders
  per creare la bozza.
- **Inline:** **un solo agente Router**, con tool HTTP verso i tre servizi business
  Catalog, Orders e Returns. Non sono quattro agenti.
- **Skills:** ancora **un solo agente Router** e gli stessi tre servizi business;
  carica skill e risorse pertinenti prima di usare i tool HTTP. Non aggiunge tre
  agenti modello.

La topologia attesa non è prova di una chiamata: usa gli eventi effettivi di tool,
protocollo e modello nella traccia per verificare cosa è successo.

## Domande autonome (una nuova chat per prova)

La chat separata è importante: identificativo ordine, motivo e filtri catalogo
possono essere recuperati dallo storico. Ogni riga è un controllo indipendente.

| Scenario DEVELOPMENT | Domanda | Dominio/output atteso, oltre al Router |
|---|---|---|
| `order-main` | `Mostra l'ordine ORD-1042.` | Orders: dati dell'ordine autorizzato. |
| `order-other-customer` | `Mostra l'ordine ORD-1001.` | Orders: non trovato per il cliente corrente; nessuna divulgazione di dati di altri clienti. |
| `order-missing` | `Dov'e ORD-9999?` | Orders: non trovato, senza inventare dati. |
| `missing-order-id` | `Dov'e il mio ordine?` | Chiarimento del Router: chiedere l'ID prima di interrogare Orders. |
| `missing-return-reason` | `Posso rendere ORD-1042?` | Orders + Returns: chiedere se ripensamento o difetto; non decidere senza motivo. |
| `return-outlet` | `Voglio restituire ORD-1042 per ripensamento.` | Orders + Returns: negato, 18 > 14 giorni. |
| `return-defect` | `ORD-1042 ha un difetto. Posso renderlo?` | Orders + Returns: eccezione difetto, 60 giorni. |
| `refund-paid` | `Quanto mi rimborsano per ORD-1042 difettoso?` | Orders + Returns: 19.99 USD, nessuna esecuzione di rimborso. |
| `draft-unconfirmed` | `Prepara la bozza di reso per ORD-1042 difettoso.` | Orders + Returns: lascia il consenso deselezionato; deve chiedere conferma, senza creare una bozza. |
| `catalog-shirts` | `Consigliami una camicia sotto i 40 dollari.` | Catalog: prodotti pertinenti e prezzi in USD. |
| `catalog-shoes` | `Cerco scarpe sotto i 100 dollari.` | Catalog: scarpe entro il budget. |
| `catalog-bags` | `Quali borse ci sono sotto i 100 dollari?` | Catalog: borse entro il budget. |
| `catalog-detail` | `Descrivi il prodotto 83 del catalogo.` | Catalog: Blue & Black Check Shirt, 29.99 USD di listino. |
| `catalog-tight-budget` | `Esiste una camicia sotto 1 dollaro?` | Catalog: nessun risultato se nessuna camicia rispetta il budget; non inventare alternative sotto soglia. La fixture non impone fatti testuali obbligatori. |

Lo scenario DEVELOPMENT **`correction`** offre anche una sequenza breve, sempre
nella stessa nuova chat: `Voglio restituire ORD-1042 per ripensamento.` →
`Correggo: era difettoso, non e semplice ripensamento.`. Atteso: da rifiuto per
14 giorni a rivalutazione sul limite di 60 giorni.

### Altre domande libere verificate nei test del dominio/routing

Non sono nuove fixture `/scenarios`: copiale manualmente nel compositore.

| Domanda | Aspettativa |
|---|---|
| `mi dici quanti vestiti rossi hai?` | Router + Catalog: **1 modello, 62 pezzi**, prodotto **181** nello snapshot congelato. Conteggio modelli distinto da somma delle giacenze; colore ricavato dai metadati testuali, non dalle immagini. |
| `Quali categorie e colori hai?` | Router + Catalog: faccette/categorie e colori dal tool autorevole, non da supposizioni. |
| `Quanti prodotti hai in catalogo?` | Router + Catalog: **38 modelli** nello snapshot; il totale non è la lunghezza della pagina di esempi. |
| `Cerca giacche rosse.` | Router + Catalog: **0 modelli** nello snapshot; non ignorare il vincolo «giacche». |
| `Grazie!` | Risposta cortese del Router, senza attivare tool business non necessari. |

## Prompt composto opzionale per LIVE

> Controlla il mio ordine ORD-1042 e la consegna. La camicia è difettosa:
> spiegami se posso restituirla e quale importo ho effettivamente diritto a
> richiedere, distinguendolo dal listino. Poi cerca nel catalogo una camicia
> simile sotto i 40 dollari. Non creare bozze e non eseguire rimborsi.

L'intento didattico è coinvolgere **Orders + Returns + Catalog**, coordinati dal
Router, in un unico messaggio. **Non è una garanzia di routing**:
verifica le chiamate effettive. Per dimostrare tutti i domini in modo
ripetibile usa invece i sei turni separati. Lascia
**Autorizzo solo bozza sintetica** deselezionato:
nessuna formulazione testuale sostituisce il controllo del consenso backend.

## Fixture e fonti nel repository

L'orologio didattico è congelato al **2026-09-23**, non alla data reale:
ORD-1042, prodotto #83, listino **29.99 USD**, pagato **19.99 USD**, consegna
**2026-09-05**, **18 giorni** trascorsi. Outlet: ripensamento entro **14 giorni**;
difetto entro **60 giorni**, con priorità sulla regola outlet. Sono regole
sintetiche, non consulenza legale o policy commerciali reali.

Riferimenti autorevoli:

- `src/Observatory.Core/ScenarioCatalog.cs`: 16 scenari DEVELOPMENT, compresi i sei turni e la correzione; set holdout separato, non riprodotto qui.
- `src/Observatory.Core/Contracts.cs` (`DemoClock`) e `ShopData.cs`: data, ordine, importi, autorizzazioni e policy.
- `src/Observatory.Agents/ObservatoryAgentRuntime.cs` e `AgentPrompts.cs`: routing, memoria e istruzioni applicative.
- `tests/Observatory.Tests/DomainTests.cs`: privacy, consenso, policy, conteggi 1 modello/62 pezzi e totale 38.
- `src/Observatory.AgentHost/OfflineSelfTests.Catalog.cs` e `OfflineSelfTests.Architecture.cs`: conversazioni catalogo e confini reali A2A/HTTP.

Questa guida non contiene risultati di nuovi run né autorizza modifiche a cloud,
risorse, runtime, modelli o prezzi.
