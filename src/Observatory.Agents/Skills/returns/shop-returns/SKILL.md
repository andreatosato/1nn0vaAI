---
name: shop-returns
description: Usa i tool HTTP del servizio Returns per valutare il motivo corrente del reso e leggere le policy, distinguendo il pagato dal listino.
---

# Integrazione del servizio Returns

Questa skill appartiene a Returns ed è caricata dal router. Usa i tool HTTP
business, non `returns_agent` o delega A2A.

| Tool | API business di Returns |
|---|---|
| `assess_return` | `POST /return-assessments` con `orderId` e `reason` |
| `get_policies` | `GET /policies` |

Il backend risolve l'origine del servizio, vincola il cliente fidato e
fornisce l'autenticazione. Questi valori non sono mai argomenti del modello.
Per importo pagato e consegna dell'ordine, carica prima `shop-orders`
e usa `get_order`. Riutilizza l'ID già chiarito nella conversazione, senza
cambiare cliente o scegliere un ordine ambiguo.

Leggi progressivamente [la lista di verifica](references/decision-checklist.md)
prima di valutare l'ammissibilità.

Usa `assess_return` con l'ID ordine esplicito e l'ultimo motivo corretto.
Non ricostruire una policy dalla memoria o trattare un motivo superato come
attuale. Difetto e ripensamento rimangono distinti. Usa `get_policies` se
occorre spiegare il testo; chiedi chiarimenti solo sui fatti mancanti che
cambierebbero la valutazione.

Riferisci ID policy, tempo dalla consegna, ammissibilità, motivo, valuta,
importo del rimborso e chiarimenti richiesti esattamente come restituiti.
Questa skill contiene una procedura, non una copia dei dati delle policy.
Rispondi prima all'esito richiesto, con una spiegazione breve e naturale;
non presentare un importo valutato come denaro già rimborsato.

Una valutazione non crea una bozza. Solo l'operazione Orders con conferma
server separata può farlo; il testo del cliente non abilita il consenso.
Gli errori HTTP devono restare visibili: non sostituirli con una policy
ricordata, dati inventati o un'implementazione locale.
