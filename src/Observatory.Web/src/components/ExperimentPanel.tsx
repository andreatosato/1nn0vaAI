import { useEffect, useRef, useState } from 'react';
import type { DemoConfiguration, ExperimentRequest, RunConfiguration, ScenarioDefinition } from '../contracts';
import { validatePreviewConfiguration } from '../lib/api';
import type { ObservatoryApi } from '../lib/api';
import { errorMessage } from '../lib/redaction';
import { ErrorBox, Icon, JsonBlock, ModeBadge, SectionHeading } from './Common';

export function ExperimentPanel({ api, server, scenarios, settings, disabled, onCompleted }: {
  api: ObservatoryApi; server: DemoConfiguration; scenarios: readonly ScenarioDefinition[];
  settings: RunConfiguration; disabled: boolean; onCompleted: () => void;
}) {
  const [modelIds, setModelIds] = useState<string[]>([]);
  const [scenarioIds, setScenarioIds] = useState<string[]>([]);
  const [repetitions, setRepetitions] = useState(1);
  const [comparePrompts, setComparePrompts] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<unknown>(undefined);
  const [sentRequest, setSentRequest] = useState<ExperimentRequest | null>(null);
  const lock = useRef(false);

  useEffect(() => {
    setModelIds((current) => current.length > 0 ? current.filter((id) => server.models.some((model) => model.id === id)) :
      server.models.filter((model) => ['gpt5', 'gpt6-astra'].includes(model.id)).map((model) => model.id));
  }, [server.models]);
  useEffect(() => {
    setScenarioIds((current) => current.length > 0 ? current.filter((id) => scenarios.some((scenario) => scenario.id === id)) :
      scenarios.filter((scenario) => scenario.id === 'main-six-turns').map((scenario) => scenario.id));
  }, [scenarios]);
  const preset = (ids: string[]) => {
    setModelIds(ids.filter((id) => server.models.some((model) => model.id === id)));
  };
  const submit = async () => {
    if (lock.current || disabled) return;
    lock.current = true;
    setBusy(true);
    setError(null);
    try {
      if (modelIds.length === 0 || scenarioIds.length === 0) throw new Error('Seleziona almeno un modello e uno scenario.');
      if (!Number.isInteger(repetitions) || repetitions < 1 || repetitions > 5) throw new Error('Scegli da 1 a 5 ripetizioni per questa UI dimostrativa.');
      const profiles = comparePrompts ? ['bad', 'good'] as const : [settings.promptProfile];
      const configurations = modelIds.flatMap((modelProfileId) => profiles.map((promptProfile) =>
        validatePreviewConfiguration({
          ...settings, modelProfileId, promptProfile, agentModels: {}, confirmAction: false,
        }, server),
      ));
      const request: ExperimentRequest = { scenarioIds, configurations, repetitions, dryRun: true };
      setSentRequest(request);
      setResult(undefined);
      const response = await api.experiment(request);
      setResult(response);
      onCompleted();
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      lock.current = false;
      setBusy(false);
    }
  };
  const blocked = busy || disabled;
  return <section className="panel view-panel experiments">
    <SectionHeading eyebrow="Confronti riproducibili" title="Prima il piano, poi l’esperimento"><ModeBadge mode={settings.mode} /></SectionHeading>
    <p className="muted">Confronta GPT-5 con GPT-6, oppure Astra/Sol con Luna. Solo profili restituiti dal backend; nessun risultato o ranking precompilato. I modelli per agente vengono azzerati per rendere il confronto esplicito.</p>
    <div className="preset-buttons"><button className="button" type="button" disabled={blocked} onClick={() => preset(['gpt5', 'gpt6-astra', 'gpt6-sol', 'gpt6-luna'])}>GPT-5 ↔ GPT-6</button><button className="button" type="button" disabled={blocked} onClick={() => preset(['gpt6-astra', 'gpt6-sol', 'gpt6-luna'])}>Astra / Sol ↔ Luna</button></div>
    <div className="experiment-options">
      <fieldset disabled={blocked}><legend>Profili modello disponibili</legend>{server.models.map((model) => <label className="checkbox-label" key={model.id}><input type="checkbox" checked={modelIds.includes(model.id)} onChange={(event) => setModelIds((current) => event.target.checked ? [...current, model.id] : current.filter((id) => id !== model.id))} /><span>{model.name}<small>{model.id}{model.configured ? ' · deployment configurato' : ' · LIVE non configurato'}</small></span></label>)}</fieldset>
      <fieldset disabled={blocked}><legend>Scenari del backend</legend>{scenarios.map((scenario) => <label className="checkbox-label" key={scenario.id}><input type="checkbox" checked={scenarioIds.includes(scenario.id)} onChange={(event) => setScenarioIds((current) => event.target.checked ? [...current, scenario.id] : current.filter((id) => id !== scenario.id))} /><span>{scenario.name}<small>{scenario.id} · {scenario.turns.length} turni · {scenario.split}</small></span></label>)}{scenarios.length === 0 && <p className="muted">Nessuno scenario caricato.</p>}</fieldset>
    </div>
    <div className="experiment-controls"><label>Ripetizioni<input type="number" min={1} max={5} step={1} value={repetitions} disabled={blocked} onChange={(event) => setRepetitions(Number(event.target.value))} /></label><label className="checkbox-label"><input type="checkbox" checked={comparePrompts} disabled={blocked || !server.promptProfiles.includes('bad') || !server.promptProfiles.includes('good')} onChange={(event) => setComparePrompts(event.target.checked)} /><span>Confronta anche prompt BAD ↔ GOOD</span></label></div>
    <div className="notice notice-info"><p><strong>Dry run:</strong> calcola il numero massimo di chiamate e non contatta modelli né crea conversazioni. Le misure LIVE si raccolgono dalla chat, con consenso per ogni singolo invio.</p></div>
    <div className="experiment-actions"><button className="button button-primary" type="button" disabled={blocked || modelIds.length === 0 || scenarioIds.length === 0} onClick={() => { void submit(); }}><Icon name="chart" />{busy ? 'Attendi…' : 'Calcola piano · dry run'}</button></div>
    {error && <ErrorBox message={error} />}
    {sentRequest && <JsonBlock value={sentRequest} label="Richiesta esperimento inviata dal browser" initiallyOpen={false} />}
    {result !== undefined && <div className="experiment-result"><h3>Piano restituito dal backend</h3><p className="muted">Il piano non equivale a un confronto misurato.</p><JsonBlock value={result} label="Risposta esperimento · JSON originale redatto" /></div>}
  </section>;
}
