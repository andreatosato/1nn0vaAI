import { useState } from 'react';
import type { ModelCallRecord, RunEvent, RunRecord } from '../contracts';
import { dateTime, duration } from '../lib/format';
import { redactText } from '../lib/redaction';
import { EmptyState, EventOrigin, JsonBlock, ModeBadge, SectionHeading } from './Common';

export function TraceView({ run, events, calls }: {
  run: RunRecord | null; events: readonly RunEvent[]; calls: readonly ModelCallRecord[];
}) {
  const [filter, setFilter] = useState('all');
  const kinds = [...new Set(events.map((event) => event.kind))];
  const shown = filter === 'all' ? events : events.filter((event) => event.kind === filter);
  const origin = run ? Date.parse(run.startedAt) : events[0] ? Date.parse(events[0].at) : null;
  const endpoints = calls.map((call) => Date.parse(call.startedAt) + call.durationMs);
  const extent = origin !== null && endpoints.length > 0 ? Math.max(1, Math.max(...endpoints) - origin) : 1;
  return <section className="panel view-panel">
    <SectionHeading eyebrow="Timeline & waterfall" title="Dal messaggio all’ultimo evento">
      {run && <ModeBadge mode={run.configuration.mode} />}
    </SectionHeading>
    <p className="muted">Timestamp e durate arrivano dal backend. Il servizio destinatario di una richiesta HTTP non è un agente modello. La durata degli strumenti non viene dedotta da un semplice evento <code>tool.called</code>.</p>
    {events.length === 0 && calls.length === 0 && <EmptyState title="Nessuna traccia selezionata">Avvia un turno dalla chat oppure apri un run nello storico. Non vengono generate tracce dimostrative fittizie.</EmptyState>}
    {calls.length > 0 && <div className="waterfall" aria-label="Durata e posizione temporale delle chiamate modello">
      <div className="waterfall-heading"><h3>Chiamate modello</h3><span>{run ? 'Origine: avvio del run' : 'Origine: primo evento disponibile'} · durate registrate</span></div>
      {calls.map((call) => {
        const offset = origin === null ? 0 : Date.parse(call.startedAt) - origin;
        const left = Math.max(0, Math.min(99.5, (offset / extent) * 100));
        const width = Math.min(100 - left, Math.max(0.5, (call.durationMs / extent) * 100));
        return <div className="waterfall-row" key={call.id}>
          <div className="waterfall-label"><strong>{call.agent}</strong><span>{call.modelProfileId}</span></div>
          <div className="waterfall-track" aria-hidden="true"><span style={{ left: `${left}%`, width: `${width}%` }} /></div>
          <div className="waterfall-time"><strong>{duration(call.durationMs)}</strong><span>inizio {offset >= 0 ? '+' : ''}{duration(offset)}</span></div>
        </div>;
      })}
      <p className="muted">Una larghezza minima rende visibili anche durate nulle o brevi; fanno fede i valori testuali.</p>
    </div>}
    {events.length > 0 && <>
      <div className="trace-controls"><h3>Eventi del run <span className="badge badge-neutral">{events.length}</span></h3><label>Filtra per tipo<select value={filter} onChange={(event) => setFilter(event.target.value)}><option value="all">Tutti gli eventi</option>{kinds.map((kind) => <option key={kind} value={kind}>{kind}</option>)}</select></label></div>
      <ol className="trace-list">{shown.map((event) => <li className="trace-event" key={event.id}>
        <div className="trace-time"><strong>{origin === null ? '—' : `${Date.parse(event.at) >= origin ? '+' : ''}${duration(Date.parse(event.at) - origin)}`}</strong><span>#{event.sequence}</span></div>
        <div className="trace-event-content"><div className="trace-event-heading"><span className={`event-kind event-${event.kind.split('.')[0]}`}>{event.kind}</span><EventOrigin event={event} /><time dateTime={event.at}>{dateTime(event.at)}</time></div><p>{redactText(event.message)}</p><JsonBlock value={event} label={`Evento JSON #${event.sequence}`} initiallyOpen={false} /></div>
      </li>)}</ol>
    </>}
  </section>;
}
