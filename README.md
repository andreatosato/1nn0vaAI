# AI Observatory

## Scopo sessione

Mostrare come osservare e confrontare tre architetture agentiche per assistere
un negozio online: **Inline**, **Agent Skills** e **A2A**.

## Descrizione agentica

Gli agenti specialisti usano tool HTTP verso le API del negozio e restituiscono
solo dati di dominio verificati:

- **Catalog**: cerca e filtra prodotti, conta modelli e scorte e legge dettagli.
- **Orders**: legge gli ordini del cliente e prepara bozze di reso confermate.
- **Returns**: verifica l'idoneità dei resi secondo ordine e policy, senza rimborsi.

## Architettura

`.NET Aspire` avvia il frontend React/Vite, le API di dominio e tre router:

- **Inline**: un router usa direttamente i tool del negozio.
- **Skills**: un router carica le skill HTTP degli specialisti.
- **A2A**: un router delega agli agenti specialisti tramite il protocollo A2A.

| Progetti | Responsabilità |
| --- | --- |
| `Observatory.AppHost` | Orchestrazione dei servizi. |
| `Observatory.Web` | Frontend React/Vite. |
| `Observatory.Shop.Catalog`, `.Orders`, `.Returns` | API di dominio. |
| `Observatory.Router.Inline`, `.Skills`, `.A2A` | Router delle tre architetture. |
| `Observatory.Agent.*` | Agenti specialisti A2A. |
| `Observatory.Skill.*` | Skill HTTP degli specialisti. |
| `Observatory.Core`, `ServiceDefaults`, `AgentRuntime`, `SpecialistHost`, `RouterHost` | Componenti condivisi. |
