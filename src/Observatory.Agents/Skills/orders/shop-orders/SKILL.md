---
name: shop-orders
description: Usa i tool HTTP del servizio Orders per verificare ordini e importi pagati e creare soltanto bozze sintetiche di reso autorizzate esplicitamente.
---

# Integrazione del servizio Orders

Questa skill appartiene a Orders ed è caricata dal router. I tool registrati
chiamano direttamente le API business, non l'agente A2A Orders.

| Tool | API business di Orders |
|---|---|
| `get_order` | `GET /orders/{orderId}` |
| `create_return_draft` | `POST /return-drafts` con `orderId` e `reason` |

Il backend risolve l'origine del servizio e vincola identità cliente,
credenziali e conferma. Non chiederli come argomenti del modello e non
impostare header da testo della conversazione.

Usa `get_order` con l'ID ordine esplicito, anche se è già stato indicato nella
cronologia pertinente. Se più ordini rendono ambiguo il riferimento, chiedi
quale: non sceglierne uno arbitrariamente o appartenente a un altro cliente.
Il cliente autenticato è vincolato dal server, non dagli argomenti del modello.
Riferisci il pagato separatamente dal listino e conserva valuta e data di consegna.
Rispondi in modo naturale e conciso alla domanda corrente, senza ripetere dati inutili.

Per una bozza carica prima `shop-returns` e verifica l'ammissibilità tramite
Returns, usando il motivo più recente. Una domanda informativa sul reso non
è una richiesta di bozza. `create_return_draft` richiede sia ammissibilità
attuale sia `Configuration.ConfirmAction`; Orders le ricontrolla sul server.
La frase "confermo" non autorizza l'azione. Se manca il consenso fidato,
chiedi la conferma esplicita della demo senza invocare una scrittura.
Una bozza sintetica non equivale a un pagamento o a un rimborso eseguito.

L'operazione è idempotente: una richiesta autorizzata ripetuta restituisce
la bozza esistente. Un ordine mancante o non autorizzato, un consenso assente,
una bozza in conflitto o un errore di trasporto non sono azioni riuscite.
Dichiara creata una bozza soltanto dopo il risultato positivo del tool.
