import { useEffect, useId, useRef, useState } from 'react';
import { defaultPromptBlocks } from '../contracts';
import type { DemoConfiguration, PromptBlocks, PromptPreview, RunConfiguration } from '../contracts';
import type { ObservatoryApi } from '../lib/api';
import { agentName } from '../lib/events';
import { errorMessage, redactText } from '../lib/redaction';
import { ErrorBox, Icon } from './Common';
import { ModelGuidance } from './ModelGuidance';

const presets: { name: string; blocks: PromptBlocks }[] = [
  { name: 'Base · tutti spenti', blocks: { ...defaultPromptBlocks } },
  { name: 'Dettagliato coerente', blocks: { ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true } },
  { name: 'Ridondante · stile', blocks: { ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true, redundancy: true } },
  { name: 'Stile in conflitto', blocks: { ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true, conflictingStyle: true } },
];

type PreviewState = { key: string } & (
  { status: 'idle' | 'loading' | 'cancelled' }
  | { status: 'ready'; value: PromptPreview }
  | { status: 'error'; message: string }
);

export function PromptLab({ api, server, settings, onChange, disabled }: {
  api: ObservatoryApi; server: DemoConfiguration; settings: RunConfiguration;
  onChange: (next: RunConfiguration) => void; disabled: boolean;
}) {
  const id = useId();
  const key = JSON.stringify({ server, settings });
  const [preview, setPreview] = useState<PreviewState>({ key, status: 'idle' });
  const generation = useRef(0);
  const pending = useRef<AbortController | null>(null);
  const current = preview.key === key && !disabled ? preview : null;

  useEffect(() => {
    setPreview({ key, status: 'idle' });
    return () => {
      generation.current++;
      pending.current?.abort();
      pending.current = null;
    };
  }, [api, key, disabled]);

  const changeBlocks = (promptBlocks: PromptBlocks) => {
    if (!disabled) onChange({ ...settings, promptBlocks, confirmAction: false });
  };
  const loadPreview = async () => {
    if (disabled) return;
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    const requestGeneration = ++generation.current;
    setPreview({ key, status: 'loading' });
    try {
      const value = await api.previewPrompt(settings, server, controller.signal);
      if (!controller.signal.aborted && requestGeneration === generation.current) setPreview({ key, status: 'ready', value });
    } catch (error) {
      if (!controller.signal.aborted && requestGeneration === generation.current) {
        setPreview({ key, status: 'error', message: errorMessage(error) });
      }
    } finally {
      if (requestGeneration === generation.current) pending.current = null;
    }
  };
  const cancelPreview = () => {
    generation.current++;
    pending.current?.abort();
    pending.current = null;
    setPreview({ key, status: 'cancelled' });
  };

  return <section className="prompt-lab" aria-labelledby={`${id}-heading`}>
    <div className="section-heading"><div><p className="eyebrow">Laboratorio prompt · prossimo turno</p><h3 id={`${id}-heading`}>Componi, leggi, poi confronta</h3></div><span className="badge badge-neutral">Nessuna inferenza nell’anteprima</span></div>
    <p>Il profilo prompt selezionato resta la base. I blocchi aggiungono istruzioni sostanziali sul server; all’inizio sono tutti spenti. <strong>Lungo non significa cattivo:</strong> checklist, formato ed esempi possono aggiungere informazioni utili, ripetizioni e contraddizioni no.</p>
    <fieldset className="prompt-blocks" disabled={disabled}>
      <legend>Blocchi opzionali del prompt</legend>
      {server.promptBlocks.map((block) => <label className={`checkbox-label prompt-block ${block.id === 'redundancy' || block.id === 'conflictingStyle' ? 'prompt-block-experiment' : ''}`} key={block.id}>
        <input type="checkbox" checked={settings.promptBlocks[block.id]} aria-labelledby={`${id}-${block.id}-label`} aria-describedby={`${id}-${block.id}-description`}
          onChange={(event) => changeBlocks({ ...settings.promptBlocks, [block.id]: event.target.checked })} />
        <span><strong id={`${id}-${block.id}-label`}>{block.label}</strong><small id={`${id}-${block.id}-description`}>{block.description}</small></span>
      </label>)}
      {server.promptBlocks.length === 0 && <p className="muted">Nessun blocco opzionale dichiarato dal backend.</p>}
    </fieldset>
    <p className="prompt-safety">Ridondanza e stile in conflitto sono controlli sperimentali sullo <strong>stile</strong>, mai su autorizzazioni, fonti o limiti di sicurezza. Nessuna scelta autorizza una bozza, un rimborso o una chiamata LIVE.</p>
    <div className="prompt-presets" role="group" aria-label="Combinazioni di blocchi prompt">
      {presets.map((preset) => <button type="button" className="button button-small" key={preset.name}
        disabled={disabled || Object.entries(preset.blocks).some(([block, enabled]) => enabled && !server.promptBlocks.some((item) => item.id === block))}
        aria-pressed={Object.entries(preset.blocks).every(([block, enabled]) => settings.promptBlocks[block as keyof PromptBlocks] === enabled)}
        onClick={() => changeBlocks({ ...preset.blocks })}>{preset.name}</button>)}
    </div>
    <p className="small-label">Le combinazioni cambiano solo i blocchi e azzerano il consenso alla bozza; conservano profilo prompt, modello, history, limiti e budget.</p>
    <section className="prompt-preview" aria-labelledby={`${id}-preview-heading`}>
      <h4 id={`${id}-preview-heading`}>Anteprima esatta delle istruzioni di base + blocchi</h4>
      <p id={`${id}-preview-scope`}>Testo restituito dal backend usando lo stesso generatore di <code>ChatOptions.Instructions</code> dell’esecuzione. <strong>Non è la richiesta completa né una cattura wire:</strong> istruzioni e risorse native delle skill, history, strumenti e risultati vengono aggiunti dopo. Per ciò che è stato realmente inviato, apri l’<a href={`#/${server.technology}/inspector`}>Inspector delle richieste</a>.</p>
      <div className="button-row">
        <button type="button" className="button button-primary" disabled={disabled || current?.status === 'loading'}
          aria-describedby={`${id}-preview-scope`} onClick={() => { void loadPreview(); }}><Icon name="code" />Genera anteprima dal server</button>
        {current?.status === 'loading' && <button type="button" className="button" onClick={cancelPreview}>Annulla anteprima</button>}
      </div>
      <p className="small-label">L’anteprima parte solo al click, non esegue modelli o strumenti e non crea conversazioni o run. È consultabile anche per configurazioni LIVE valide prima dell’autorizzazione all’esecuzione.</p>
      <p className="small-label">Le istruzioni arrivano dal backend: il browser non le traduce né le riscrive. Cambiare il modello non garantisce un testo diverso; usa il profilo prompt e i blocchi per scegliere le istruzioni del prossimo messaggio.</p>
      {current?.status === 'loading' && <p role="status">Caricamento dell’anteprima dal backend…</p>}
      {current?.status === 'cancelled' && <p role="status">Anteprima annullata. Nessun modello avviato.</p>}
      {(!current || current.status === 'idle') && <p className="muted">Genera l’anteprima per la configurazione corrente. Ogni modifica invalida quella precedente.</p>}
      {current?.status === 'error' && <ErrorBox title="Anteprima non disponibile" message={current.message} retry={() => { void loadPreview(); }} />}
      {current?.status === 'ready' && <div className="prompt-preview-result">
        <p className="notice notice-info" role="status">{redactText(current.value.notice)}</p>
        <p className="small-label">Conteggi in caratteri dichiarati dal backend, non stime di token. Eventuali segreti sono oscurati difensivamente nella UI.</p>
        {current.value.agents.map((agent) => <details className="prompt-preview-agent" key={agent.agent} open={agent.agent === 'router'}>
          <summary id={`${id}-instructions-${agent.agent}`}>{agentName(agent.agent) ?? agent.agent} · {agent.characterCount.toLocaleString('it-IT')} caratteri</summary>
          <pre tabIndex={0} aria-labelledby={`${id}-instructions-${agent.agent}`}>{redactText(agent.instructions)}</pre>
        </details>)}
      </div>}
    </section>
    <ModelGuidance />
  </section>;
}
