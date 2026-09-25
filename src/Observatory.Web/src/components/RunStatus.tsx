import type { DemoState } from '../hooks/useDemo';
import { money, shortId, statusLabel, tokens } from '../lib/format';
import { redactText } from '../lib/redaction';
import { ErrorBox, Icon, ModeBadge } from './Common';
import { ConfigurationSummary } from './ConfigurationSummary';

export function RunStatus({ demo, compact = false }: { demo: DemoState; compact?: boolean }) {
  const currentRun = demo.monitor.record;
  return <div className={`run-status${compact ? ' run-status-compact' : ''}`}>
    <section className="run-toolbar" aria-label="Run selezionato">
      <div className="run-identity"><span className={`run-indicator ${demo.running ? 'run-indicator-active' : ''}`} aria-hidden="true" /><div><span className="small-label">Run selezionato</span><strong>{demo.tracked ? shortId(demo.tracked.runId) : 'Nessuno · attendi il primo invio'}</strong></div>{currentRun && <><span className="badge badge-neutral">{statusLabel(currentRun.status)}</span><ModeBadge mode={currentRun.configuration.mode} replay={currentRun.status === 'replayed'} /></>}</div>
      {demo.tracked && <div className="run-actions"><span className="connection-label">{demo.monitor.connection === 'streaming' ? 'SSE connesso' : demo.monitor.connection === 'polling' ? 'Polling GET' : demo.monitor.connection === 'connecting' ? 'Connessione SSE…' : demo.monitor.connection === 'closed' ? 'Registrazione completa' : demo.monitor.connection === 'error' ? 'Lettura interrotta' : 'Lettura stato…'}</span><button type="button" className="button button-small" onClick={demo.monitor.refresh}><Icon name="refresh" />Stato GET</button>{demo.running && <><button type="button" className="button button-small" onClick={demo.monitor.reconnect}>Riconnetti SSE</button><button type="button" className="button button-small button-danger" disabled={demo.actionBusy || currentRun?.status === 'cancelling'} onClick={() => { void demo.cancel(); }}>Annulla run</button></>}</div>}
    </section>
    {currentRun && <div className="recorded-configuration">
      <ConfigurationSummary title="Impostazioni registrate nel run" settings={currentRun.configuration} server={demo.configuration.data} />
      <p className="config-notice">Snapshot letto dal run salvato: le opzioni del prossimo messaggio non lo modificano. Le chiamate effettive, distinte dalla configurazione richiesta, si verificano nell’Inspector.</p>
      <p className="config-notice" aria-label="Consumi del run selezionato">
        Input: <strong>{tokens(currentRun.inputTokens)}</strong>
        {' · '}Output: <strong>{tokens(currentRun.outputTokens)}</strong>
        {' · '}Costo stimato: <strong>{money(currentRun.estimatedCostUsd)}</strong>
        {' · '}<a href={`#/${currentRun.technology}/usage`}>Dettaglio per modello</a>
        {demo.running && ' · Dati provvisori'}
      </p>
    </div>}
    {demo.monitor.notice && demo.monitor.connection !== 'closed' && <div className="notice notice-info" role="status"><p>{demo.monitor.notice}</p></div>}
    {demo.monitor.error && <ErrorBox message={demo.monitor.error} retry={demo.monitor.refresh} title="Lettura run incompleta: nessun turno reinviato" />}
    {currentRun?.error && <ErrorBox message={redactText(currentRun.error)} title={`Run ${statusLabel(currentRun.status).toLowerCase()}`} />}
    {demo.actionError && <ErrorBox message={demo.actionError} title={demo.failedSubmission ? 'Invio non confermato' : 'Operazione non riuscita'} />}
    {demo.failedSubmission && <div className="notice notice-warning"><div><strong>Non viene effettuato alcun reinvio automatico.</strong><p>Puoi ritentare esplicitamente lo stesso payload con la medesima chiave di idempotenza, oppure verificarne l’esito nello storico.</p><code>Idempotency key: {demo.failedSubmission.request.idempotencyKey}</code></div><div className="button-row"><button type="button" className="button" disabled={demo.actionBusy} onClick={() => { void demo.retrySubmission(); }}>Riprova lo stesso invio</button><button type="button" className="button" disabled={demo.actionBusy} onClick={demo.discardSubmission}>Scarta il tentativo locale</button></div></div>}
    {demo.actionNotice && <div className="notice notice-info" role="status"><p>{demo.actionNotice}</p></div>}
  </div>;
}
