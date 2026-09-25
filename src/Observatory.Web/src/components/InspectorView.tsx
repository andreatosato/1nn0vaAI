import { useState } from 'react';
import type { ModelCallRecord } from '../contracts';
import type { HttpCapture } from '../lib/api';
import { dateTime, duration, shortId } from '../lib/format';
import { redactText } from '../lib/redaction';
import { EmptyState, JsonBlock, ModeBadge, SectionHeading } from './Common';

function captureDescription(kind: string): string {
  if (kind === 'wire') return 'WIRE: payload HTTP registrato dal backend. Le sezioni logical restano distinte dai tentativi di trasporto.';
  if (kind === 'logical') return 'LOGICAL: richiesta logica dell’SDK/orchestratore. Non implica cattura del traffico HTTP wire.';
  return `Tipo dichiarato dal backend: ${kind}. Nessuna equivalenza con una cattura wire viene presunta.`;
}

export function InspectorView({ calls, captures }: {
  calls: readonly ModelCallRecord[]; captures: readonly HttpCapture[];
}) {
  const [callId, setCallId] = useState('');
  const [httpId, setHttpId] = useState('');
  const selected = calls.find((call) => call.id === callId) ?? calls[0];
  const capture = captures.find((entry) => entry.id === httpId) ?? captures.at(-1);
  return <section className="panel view-panel">
    <SectionHeading eyebrow="Fatti, non ricostruzioni" title="Che cosa è stato realmente registrato" />
    <p className="muted">Prompt, history, strumenti e risposte provengono dalla registrazione backend. Credenziali e header sensibili vengono oscurati anche nel browser.</p>
    {calls.length === 0 ? <EmptyState title="Nessuna chiamata modello registrata" icon="code">Quando compare un evento model.completed o un run completo, puoi ispezionare i suoi payload. Non viene sintetizzato un prompt a partire dalla UI.</EmptyState> :
      <div className="inspector-layout">
        <div className="call-list" role="group" aria-label="Scegli una chiamata modello">{calls.map((call, index) => <button type="button" className={selected?.id === call.id ? 'selected' : ''} aria-pressed={selected?.id === call.id} key={call.id} onClick={() => setCallId(call.id)}>
          <span className="call-number">{index + 1}</span><span><strong>{call.agent}</strong><span>{call.modelProfileId}</span><small>{duration(call.durationMs)} · {call.captureKind}</small></span>
        </button>)}</div>
        {selected && <div className="inspector-detail">
          <div className="inspector-title"><h3>{selected.agent} · {shortId(selected.id)}</h3><ModeBadge mode={selected.mode} /><span className="badge badge-neutral">{selected.captureKind}</span></div>
          <p className="capture-description">{captureDescription(selected.captureKind)}</p>
          <dl className="metadata-grid"><div><dt>Inizio</dt><dd>{dateTime(selected.startedAt)}</dd></div><div><dt>Modello dichiarato</dt><dd>{selected.modelId ?? 'Non disponibile'}</dd></div><div><dt>Trace ID</dt><dd>{selected.traceId ?? 'Non disponibile'}</dd></div><div><dt>Span ID</dt><dd>{selected.spanId ?? 'Non disponibile'}</dd></div><div><dt>Provider response ID</dt><dd>{selected.providerResponseId ?? 'Non disponibile'}</dd></div><div><dt>Origine usage</dt><dd>{selected.usageSource}</dd></div></dl>
          {selected.error && <p className="inline-error" role="alert">{redactText(selected.error)}</p>}
          <JsonBlock value={selected.request} label="Richiesta registrata · istruzioni, history e tool" />
          <JsonBlock value={selected.response} label="Risposta registrata" />
          <JsonBlock value={selected.rawUsage} label="Usage grezzo del provider" initiallyOpen={false} />
          <h3>Tentativi wire</h3>
          {selected.attempts.length === 0 ? <p className="muted">Nessun tentativo wire registrato. Non è corretto ricostruirlo dalla richiesta logica.</p> : selected.attempts.map((attempt, index) => <div className="wire-attempt" key={attempt.id}>
            <h4>Tentativo {index + 1} · HTTP {attempt.statusCode ?? 'non registrato'} · {duration(attempt.durationMs)}</h4>
            {attempt.truncated && <p className="notice notice-warning">Il backend segnala una cattura troncata.</p>}
            <JsonBlock value={attempt} label={`Tentativo wire ${index + 1} · endpoint e corpi originali`} initiallyOpen={false} />
          </div>)}
        </div>}
      </div>}
    <details className="browser-captures" open={calls.length === 0}>
      <summary>Richieste effettive del browser verso l’API · non sono chiamate wire al modello</summary>
      <p className="muted">Ultime {captures.length} richieste fetch della sessione (massimo 40). Non vengono registrati cookie o header di autenticazione. Lo stream SSE è consultabile in Traccia.</p>
      {capture ? <>
        <label>Richiesta HTTP<select value={capture.id} onChange={(event) => setHttpId(event.target.value)}>{[...captures].reverse().map((entry) => <option key={entry.id} value={entry.id}>{entry.method} {entry.path} · {entry.status ?? 'rete'} · {dateTime(entry.at)}</option>)}</select></label>
        <p><code>{capture.method} {capture.path}</code> · HTTP {capture.status ?? 'nessuna risposta'}{capture.responseContentType ? ` · ${capture.responseContentType}` : ''}</p>
        {capture.error && <p className="inline-error">{capture.error}</p>}
        <JsonBlock value={capture.request} label="Corpo inviato dal browser (null = nessun corpo)" />
        <JsonBlock value={capture.response} label={capture.responseContentType?.toLowerCase().startsWith('text/event-stream') ? 'Stream SSE ricevuto · eventi decodificati e header di provenienza' : 'Corpo ricevuto dal backend'} initiallyOpen={false} />
      </> : <p className="muted">Nessuna richiesta acquisita.</p>}
    </details>
  </section>;
}
