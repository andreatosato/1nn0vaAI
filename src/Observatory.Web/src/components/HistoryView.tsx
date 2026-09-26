import { useState } from 'react';
import type { RunConfiguration } from '../contracts';
import type { DemoState } from '../hooks/useDemo';
import { dateTime, duration, money, shortId, statusLabel } from '../lib/format';
import { redactText } from '../lib/redaction';
import { EmptyState, ErrorBox, Icon, JsonBlock, ModeBadge, SectionHeading } from './Common';
import { ExperimentPanel } from './ExperimentPanel';

export function HistoryView({ demo, settings }: { demo: DemoState; settings: RunConfiguration | null }) {
  const [confirmClear, setConfirmClear] = useState(false);
  const runs = [...(demo.runs.data ?? [])].sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt));
  return <div className="stack">
    <section className="panel view-panel">
      <SectionHeading eyebrow="Persistenza backend" title="Storico delle esecuzioni"><div className="table-actions">
        <button type="button" className="button button-small" disabled={demo.runs.loading} onClick={demo.runs.reload}><Icon name="refresh" />Aggiorna</button>
        <button type="button" className="button button-small button-danger" disabled={demo.runs.loading || demo.busy || demo.actionBusy || demo.failedSubmission !== null || runs.length === 0}
          onClick={() => setConfirmClear(true)}>Cancella tutte</button>
      </div></SectionHeading>
      <p className="muted">Seleziona un run per aprire mappa, traccia, richieste e usage. Export e replay usano le API di registrazione; non rigenerano risultati fittizi.</p>
      {confirmClear && <div className="notice notice-warning" role="alert">
        <p>Elimina le esecuzioni, le tracce e i report esperimenti di questa demo. I testi delle conversazioni restano. L’operazione non si può annullare.</p>
        <div className="button-row">
          <button type="button" className="button button-danger" disabled={demo.actionBusy}
            onClick={() => { setConfirmClear(false); void demo.clearRunHistory(); }}>Conferma cancellazione</button>
          <button type="button" className="button" onClick={() => setConfirmClear(false)}>Annulla</button>
        </div>
      </div>}
      {demo.runs.error && <ErrorBox message={demo.runs.error} retry={demo.runs.reload} />}
      {demo.runs.loading && <p role="status">Lettura run dal backend…</p>}
      {demo.runs.data && runs.length === 0 && <EmptyState title="Lo storico è ancora vuoto" icon="history">Il primo turno inviato apparirà qui. Nessuna conversazione dimostrativa viene preinserita dal frontend.</EmptyState>}
      {runs.length > 0 && <div className="table-scroll" tabIndex={0} aria-label="Storico dei run"><table><caption>Run della demo corrente · nessuna aggregazione tra tecnologie diverse</caption><thead><tr><th scope="col">Richiesta / run</th><th scope="col">Profilo / modalità</th><th scope="col">Stato</th><th scope="col">Durata</th><th scope="col">Costo USD</th><th scope="col">Azioni</th></tr></thead><tbody>{runs.map((run) => <tr key={run.id} className={demo.tracked?.runId === run.id ? 'selected-row' : ''}>
        <th scope="row"><span className="history-message">{redactText(run.message)}</span><code title={run.id}>{shortId(run.id)}</code><small>{dateTime(run.startedAt)}</small></th>
        <td>{run.configuration.modelProfileId}<small>{run.configuration.promptProfile} · {run.configuration.historyStrategy}</small><ModeBadge mode={run.configuration.mode} replay={run.status === 'replayed'} /></td>
        <td>{statusLabel(run.status)}</td><td>{duration(run.durationMs)}</td><td>{money(run.estimatedCostUsd)}<small>{run.costStatus}</small></td>
        <td><div className="table-actions"><button className="button button-small" type="button" disabled={demo.busy} onClick={() => { void demo.selectRun(run.id); }}>{demo.tracked?.runId === run.id ? 'Selezionato' : 'Seleziona'}</button><button className="button button-small" type="button" disabled={demo.actionBusy} onClick={() => { void demo.exportRun(run.id); }} aria-label={`Esporta JSON redatto del run ${run.id}`}><Icon name="download" />JSON</button><button className="button button-small" type="button" disabled={demo.busy || !['completed', 'failed', 'cancelled', 'canceled', 'replayed'].includes(run.status)} onClick={() => { void demo.replay(run.id); }} aria-label={`Riproduci la registrazione del run ${run.id}`}><Icon name="play" />Replay</button></div></td>
      </tr>)}</tbody></table></div>}
      {demo.replaySource && <div className="replay-result">
        <div className="replay-title"><h3>Replay della registrazione originale</h3><ModeBadge mode={demo.replaySource.configuration.mode} replay /></div>
        <p>Run <code>{demo.replaySource.id}</code> · nessun nuovo run, nessuna nuova inferenza. I tempi visualizzati appartengono alla registrazione originale, non alla durata della riproduzione.</p>
        <p role="status">{demo.replayStatus === 'receiving' ? 'Ricezione SSE in corso' : demo.replayStatus === 'completed' ? 'Replay SSE completato' : 'Riproduzione interrotta'} · {demo.replayEvents.length} eventi ricevuti.</p>
        <ol className="replay-event-list" aria-label="Eventi ricevuti dal replay">{demo.replayEvents.slice(-12).map((event, index) => <li key={`${event.id}-${index}`}><span className="event-kind">{event.kind}</span><strong>{event.agent}</strong><time dateTime={event.at}>{dateTime(event.at)}</time><span>{redactText(event.message)}</span></li>)}</ol>
        {demo.replayEvents.length > 12 && <p className="muted">Elenco degli ultimi 12 eventi; tutti gli eventi ricevuti sono nel JSON sottostante.</p>}
        {demo.replayResult
          ? <JsonBlock value={demo.replayResult} label="Replay SSE decodificato · eventi originali e header di provenienza" initiallyOpen={false} />
          : demo.replayEvents.length > 0 && <JsonBlock value={demo.replayEvents} label="Eventi effettivamente ricevuti · flusso non ancora completo" initiallyOpen={false} />}
      </div>}
    </section>
    {demo.scenarios.error && <ErrorBox message={demo.scenarios.error} retry={demo.scenarios.reload} title="Scenari esperimento non disponibili" />}
    {demo.configuration.data && settings && <ExperimentPanel key={demo.historyVersion} api={demo.api} server={demo.configuration.data} scenarios={demo.scenarios.data ?? []} settings={settings} disabled={demo.busy} onCompleted={() => { demo.runs.reload(); demo.conversations.reload(); }} />}
  </div>;
}
