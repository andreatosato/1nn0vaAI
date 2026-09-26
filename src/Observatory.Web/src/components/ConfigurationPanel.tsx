import { useId } from 'react';
import { agentIds, agents, defaultPromptBlocks, historyStrategySchema, promptProfileSchema } from '../contracts';
import type { DemoConfiguration, RunConfiguration } from '../contracts';
import type { ObservatoryApi } from '../lib/api';
import { liveModelUnavailableReason } from '../lib/api';
import { agentName } from '../lib/events';
import { redactText } from '../lib/redaction';
import { promptExamples } from '../metadata';
import { ModeBadge } from './Common';
import { PromptLab } from './PromptLab';

export function initialSettings(server: DemoConfiguration): RunConfiguration | null {
  const model = server.models.find((item) => item.id === 'gpt5') ?? server.models[0];
  const prompt = promptProfileSchema.safeParse(
    server.promptProfiles.includes('good') ? 'good' : server.promptProfiles[0],
  );
  const history = historyStrategySchema.safeParse(
    server.historyStrategies.includes('full') ? 'full' : server.historyStrategies[0],
  );
  if (!model || !prompt.success || !history.success) return null;
  return {
    mode: 'live',
    modelProfileId: model.id, agentModels: {}, promptProfile: prompt.data,
    promptBlocks: { ...defaultPromptBlocks },
    historyStrategy: history.data, toolTransport: 'direct', confirmAction: false,
    maxOutputTokens: 1500, maxModelCalls: 24, approvedBudgetUsd: null, unboundedExecution: true,
  };
}

export function reconcileSettings(previous: RunConfiguration | null, server: DemoConfiguration): RunConfiguration | null {
  if (!previous || !server.models.some((model) => model.id === previous.modelProfileId)) return initialSettings(server);
  const agentModels = Object.fromEntries(Object.entries(previous.agentModels).filter(([agent, modelId]) =>
    server.capabilities.agentNames.some((name) => name === agent) && server.models.some((model) => model.id === modelId),
  ));
  const overridesRemoved = Object.keys(agentModels).length !== Object.keys(previous.agentModels).length;
  return {
    ...previous, agentModels,
    mode: 'live',
    confirmAction: overridesRemoved ? false : previous.confirmAction,
  };
}

export function ConfigurationPanel({ api, server, settings, onChange, disabled }: {
  api: ObservatoryApi; server: DemoConfiguration; settings: RunConfiguration;
  onChange: (next: RunConfiguration) => void; disabled: boolean;
}) {
  const id = useId();
  const update = (change: Partial<RunConfiguration>) => {
    if (disabled) return;
    const next = reconcileSettings({ ...settings, ...change }, server);
    if (next) onChange(next);
  };
  const executionAgents = agents.filter((agent) => server.capabilities.agentNames.includes(agentIds[agent]));
  const overrides = Object.entries(settings.agentModels).filter(([agent]) => server.capabilities.agentNames.some((name) => name === agent));
  const example = promptExamples[settings.promptProfile];
  return <section className="configuration panel" aria-label="Configurazione del prossimo turno">
    <div className="configuration-heading"><h2>Prossimo turno</h2><ModeBadge mode={settings.mode} /><span>Nessuna esecuzione al cambio delle opzioni</span></div>
    <div className="configuration-help" id={`${id}-application`}>
      <p><strong>Il bot usa le nuove impostazioni dal prossimo “Invia”.</strong> Modello, profilo prompt e blocchi si applicano al prossimo messaggio inviato. Non cambiano il run in corso né i run salvati: ciascuno conserva le proprie impostazioni.</p>
      <p>La conversazione e i messaggi precedenti restano. Per un confronto controllato, apri il bot e usa <strong>Nuova chat</strong> per ogni prova: ripeti gli stessi messaggi nello stesso ordine e cambia una sola variabile alla volta.</p>
    </div>
    <fieldset disabled={disabled} className="configuration-grid" aria-describedby={`${id}-application`}>
      <legend className="sr-only">Modelli e contesto del prossimo turno</legend>
      <div className="field-label">Modalità<span className="badge badge-live">LIVE</span></div>
      <label>Profilo modello<select value={settings.modelProfileId} aria-describedby={`${id}-independent`} onChange={(event) => update({ modelProfileId: event.target.value })}>
        {server.models.map((model) => {
          const reason = settings.mode === 'live' ? liveModelUnavailableReason(model, server) : null;
          return <option key={model.id} value={model.id} disabled={Boolean(reason)} title={reason ?? undefined}>{model.name}{reason ? ' · non disponibile in LIVE' : ''}</option>;
        })}
      </select></label>
      <label>Profilo prompt<select value={settings.promptProfile} aria-describedby={`${id}-independent`} onChange={(event) => {
        const parsed = promptProfileSchema.safeParse(event.target.value);
        if (parsed.success) update({ promptProfile: parsed.data });
      }}>{server.promptProfiles.map((profile) => {
        const parsed = promptProfileSchema.safeParse(profile);
        return parsed.success ? <option key={profile} value={profile}>{promptExamples[parsed.data].name}</option> : null;
      })}</select></label>
      <label>Memoria conversazione<select value={settings.historyStrategy} onChange={(event) => {
        const parsed = historyStrategySchema.safeParse(event.target.value);
        if (parsed.success) update({ historyStrategy: parsed.data });
      }}>{server.historyStrategies.map((strategy) => {
        const parsed = historyStrategySchema.safeParse(strategy);
        return parsed.success ? <option key={strategy} value={strategy}>{strategy === 'full' ? 'Full · history completa' : 'Compact · history compatta'}</option> : null;
      })}</select></label>
    </fieldset>
    <p className="config-notice" id={`${id}-independent`}>Modello e prompt sono scelte indipendenti: passare da GPT-5 a GPT-6 non cambia da solo il profilo prompt, e passare da BAD a GOOD non cambia il modello. Il profilo modello vale per gli agenti senza override.</p>
    {overrides.length > 0 && <div className="notice notice-warning configuration-overrides">
      <div><strong>Attenzione: ci sono override per agente.</strong><p>{overrides.map(([agent, model]) => `${agentName(agent) ?? redactText(agent)}: ${redactText(server.models.find((item) => item.id === model)?.name ?? model)}`).join(' · ')}. Queste scelte prevalgono sul profilo modello, anche quando lo cambi.</p></div>
      <button type="button" className="button button-small" disabled={disabled} onClick={() => update({ agentModels: {}, confirmAction: false })}>Usa il profilo modello per tutti gli agenti</button>
    </div>}
    <p className="config-notice"><strong>LIVE usa chiamate reali.</strong> La demo esegue senza limiti applicativi; restano necessari backend e modello abilitati, oltre al consenso al singolo invio.</p>
    {disabled && <p className="config-notice">Impostazioni bloccate durante invio, esecuzione o invio dall’esito incerto. Un retry riutilizza il payload già inviato, non nuove impostazioni.</p>}
    {!server.allowLive && <p className="config-notice" role="status">LIVE non è pronto: il backend deve abilitare l'inferenza e verificare deployment, prezzi e capacità dei modelli. Nessuna richiesta verrà reindirizzata a un provider alternativo.</p>}
    <PromptLab api={api} server={server} settings={settings} onChange={update} disabled={disabled} />
    <details className="advanced-settings">
      <summary>Modelli per agente, limiti ed esempi di prompt</summary>
      <p className="config-notice">{server.capabilities.businessApi
        ? 'Solo il Router esegue il modello. Catalog, Orders e Returns sono servizi HTTP, non modelli da configurare.'
        : 'Il Router e i tre agenti remoti A2A eseguono modelli configurabili separatamente.'} Gli agenti configurabili sono dichiarati da <code>/config</code>.</p>
      <fieldset disabled={disabled} className="advanced-grid">
        <legend className="sr-only">Configurazione avanzata</legend>
        {executionAgents.map((agent) => <label key={agent}>{agent}<select value={settings.agentModels[agentIds[agent]] ?? ''} onChange={(event) => {
          const next = { ...settings.agentModels };
          if (event.target.value) next[agentIds[agent]] = event.target.value;
          else delete next[agentIds[agent]];
          update({ agentModels: next });
        }}><option value="">Usa il profilo modello scelto</option>{server.models.map((model) => {
          const reason = settings.mode === 'live' ? liveModelUnavailableReason(model, server) : null;
          return <option key={model.id} value={model.id} disabled={Boolean(reason)} title={reason ?? undefined}>{model.name}{reason ? ' · non disponibile in LIVE' : ''}</option>;
        })}</select></label>)}
        <p className="config-notice">La demo esegue senza limiti applicativi.</p>
      </fieldset>
      <div className="prompt-example"><strong>{example.name}</strong><p>{example.description}</p><blockquote>{example.example}</blockquote><p className="muted">Esempio illustrativo, non una copia del prompt inviato. Le istruzioni e la history effettive dipendono dalla configurazione registrata nel run.</p></div>
    </details>
  </section>;
}
