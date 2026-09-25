import { useMemo, useState } from 'react';
import type { RunRecord, Technology } from '../contracts';
import { useRemote } from '../hooks/useRemote';
import { ObservatoryApi } from '../lib/api';
import { duration, money, statusLabel, tokens } from '../lib/format';
import { redactText } from '../lib/redaction';
import { EmptyState, ErrorBox, Icon, ModeBadge, SectionHeading } from './Common';
import { demoMetadata } from '../metadata';

const apis: Record<Technology, ObservatoryApi> = {
  inline: new ObservatoryApi('inline'),
  skills: new ObservatoryApi('skills'),
  a2a: new ObservatoryApi('a2a'),
};

function requestKey(message: string): string {
  return message.trim().replace(/\s+/g, ' ');
}

function comparableCost(run: RunRecord): number | null {
  return run.configuration.mode === 'live'
    && (run.costStatus === 'priced' || run.costStatus === 'estimated')
    ? run.estimatedCostUsd
    : null;
}

function successfulLiveRun(run: RunRecord): boolean {
  return run.configuration.mode === 'live' && ['completed', 'replayed'].includes(run.status.toLowerCase());
}

function runLabel(run: RunRecord): string {
  return `${demoMetadata[run.technology].title} · ${run.configuration.modelProfileId} · ${run.configuration.promptProfile}`;
}

export function MeasureComparisonView() {
  const inline = useRemote(apis.inline.runs);
  const skills = useRemote(apis.skills.runs);
  const a2a = useRemote(apis.a2a.runs);
  const [selectedRequest, setSelectedRequest] = useState('');
  const sources = [inline, skills, a2a] as const;
  const requestGroups = useMemo(() => {
    const groups = new Map<string, RunRecord[]>();
    for (const run of [inline.data, skills.data, a2a.data].flatMap((runs) => runs ?? [])) {
      const key = requestKey(run.message);
      if (!key) continue;
      groups.set(key, [...(groups.get(key) ?? []), run]);
    }
    return [...groups].sort(([, left], [, right]) => {
      const count = right.length - left.length;
      return count || Date.parse(right[0]?.startedAt ?? '') - Date.parse(left[0]?.startedAt ?? '');
    });
  }, [inline.data, skills.data, a2a.data]);
  const allRuns = [inline.data, skills.data, a2a.data].flatMap((runs) => runs ?? []);
  const selectedKey = requestGroups.some(([key]) => key === selectedRequest)
    ? selectedRequest
    : requestGroups[0]?.[0] ?? '';
  const selectedRuns = (requestGroups.find(([key]) => key === selectedKey)?.[1] ?? [])
    .slice()
    .sort((left, right) => Date.parse(left.startedAt) - Date.parse(right.startedAt));
  const successful = selectedRuns.filter(successfulLiveRun);
  let fastest: RunRecord | null = null;
  let cheapest: RunRecord | null = null;
  for (const run of successful) {
    if (run.durationMs !== null && (fastest === null || run.durationMs < (fastest.durationMs ?? Infinity))) fastest = run;
    const cost = comparableCost(run);
    const lowestCost = cheapest === null ? null : comparableCost(cheapest);
    if (cost !== null && (cheapest === null || (lowestCost !== null && cost < lowestCost))) cheapest = run;
  }
  const loading = sources.some((source) => source.loading);
  const hasError = sources.some((source) => source.error !== null);
  const reloadAll = () => sources.forEach((source) => source.reload());

  return <main id="main-content" className="home-page" tabIndex={-1}>
    <section className="demo-heading">
      <div><p className="eyebrow">Evidenze registrate · stessa richiesta</p>
        <h1 id="page-heading" tabIndex={-1}>Confronto misure<span className="heading-separator">/</span><span className="heading-secondary">Tempi e costi</span></h1>
        <p>Confronta run reali di Inline, Agent Skills e A2A usando lo stesso testo inviato. La pagina legge lo storico dei tre backend: non avvia inferenze e non stima dati mancanti.</p>
      </div>
    </section>
    <section className="panel view-panel">
      <SectionHeading eyebrow="Solo run salvati" title="Scegli la richiesta">
        <button type="button" className="button button-small" disabled={loading} onClick={reloadAll}><Icon name="refresh" />Aggiorna</button>
      </SectionHeading>
      <div className="notice notice-info">
        <p>Si raggruppano richieste con lo stesso testo, ignorando solo spazi iniziali/finali e spaziature ripetute. Per un confronto controllato mantieni uguali dati, modalità e impostazioni, cambiando una sola variabile; ripeti i run per considerare la variabilità.</p>
        <p>Il tempo è quello end-to-end registrato dal backend; il primo output è mostrato separatamente. Il costo è la stima salvata per il run, non una fattura. I dati incompleti restano indisponibili e non vengono mostrati come zero.</p>
      </div>
      {loading && <p role="status">Lettura dello storico delle tre demo…</p>}
      {hasError && <ErrorBox title="Storico parziale o non disponibile"
        message={sources.map((source, index) => source.error ? `${(['Inline', 'Agent Skills', 'A2A'] as const)[index]}: ${source.error}` : null).filter(Boolean).join(' · ')}
        retry={reloadAll} />}
      {!loading && allRuns.length === 0 && !hasError
        ? <EmptyState title="Non ci sono ancora misure" icon="chart">Esegui la stessa richiesta nelle demo e attendi il completamento dei run. Poi torna qui e aggiorna: compariranno tempi, modelli, token e costi registrati.</EmptyState>
        : requestGroups.length > 0 && <>
          <label htmlFor="comparison-request">Richiesta identica
            <select id="comparison-request" value={selectedKey} onChange={(event) => setSelectedRequest(event.target.value)}>
              {requestGroups.map(([key, runs]) => <option key={key} value={key}>{redactText(key)} · {runs.length} {runs.length === 1 ? 'esecuzione' : 'esecuzioni'}</option>)}
            </select>
          </label>
          <p className="small-label">Testo richiesto: <strong>{redactText(selectedKey)}</strong> · {selectedRuns.length} misurazioni trovate</p>
          {(fastest || cheapest) && <div className="metrics-grid">
            <div className="metric"><span>Run LIVE completati più rapido</span>
              <strong>{fastest ? duration(fastest.durationMs) : 'Non disponibile'}</strong>
              {fastest && <small>{runLabel(fastest)}</small>}
            </div>
            <div className="metric"><span>Costo LIVE registrato più basso</span>
              <strong>{cheapest ? money(comparableCost(cheapest), 'USD') : 'Non disponibile'}</strong>
              {cheapest && <small>{runLabel(cheapest)}</small>}
            </div>
          </div>}
          <div className="table-scroll" tabIndex={0} aria-label="Confronto dei run per la stessa richiesta">
            <table>
              <caption>Run della richiesta selezionata · ordinati dal meno al più recente</caption>
              <thead><tr>
                <th scope="col">Demo</th><th scope="col">Modello / prompt</th><th scope="col">Modalità</th>
                <th scope="col">Stato</th><th scope="col">Tempo run</th><th scope="col">Primo output</th>
                <th scope="col">Input token</th><th scope="col">Output token</th><th scope="col">Costo USD</th>
              </tr></thead>
              <tbody>{selectedRuns.map((run) => <tr key={`${run.technology}-${run.id}`}>
                <th scope="row">{demoMetadata[run.technology].title}<small>{new Date(run.startedAt).toLocaleString('it-IT')}</small></th>
                <td><strong>{run.configuration.modelProfileId}</strong><small>{run.configuration.promptProfile} · history {run.configuration.historyStrategy}</small></td>
                <td><ModeBadge mode={run.configuration.mode} replay={run.status === 'replayed'} /></td>
                <td>{statusLabel(run.status)}</td><td>{duration(run.durationMs)}</td><td>{duration(run.timeToFirstAnswerMs)}</td>
                <td>{tokens(run.inputTokens)}</td><td>{tokens(run.outputTokens)}</td>
                <td>{money(comparableCost(run))}<small>{run.costStatus}</small></td>
              </tr>)}</tbody>
            </table>
          </div>
        </>}
      {hasError && allRuns.length === 0 && <p className="muted">Nessun dato viene inventato per i backend non raggiungibili.</p>}
    </section>
  </main>;
}
