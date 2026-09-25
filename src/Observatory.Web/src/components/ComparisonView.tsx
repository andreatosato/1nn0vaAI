import type { DemoConfiguration } from '../contracts';
import type { DemoState } from '../hooks/useDemo';
import { comparisonPresets, comparisonSettings, prepareComparison } from '../lib/comparisonPresets';
import type { PreparedComparison } from '../lib/comparisonPresets';
import { errorMessage } from '../lib/redaction';
import { ErrorBox, SectionHeading } from './Common';
import { ConfigurationSummary } from './ConfigurationSummary';

export function ComparisonView({ server, scenarios, disabled, needsNewChat, onApply, onOpenChat }: {
  server: DemoConfiguration; scenarios: DemoState['scenarios']; disabled: boolean; needsNewChat: boolean;
  onApply: (preset: PreparedComparison) => void; onOpenChat: () => void;
}) {
  return <div className="stack comparison-view">
    <section className="panel view-panel">
      <SectionHeading eyebrow="Tre prove ripetibili" title="Confronti preimpostati">
        <button type="button" className="button button-small" onClick={onOpenChat}>Apri chat</button>
      </SectionHeading>
      <p>Scegli una variante e premi <strong>Prepara</strong>: configura il bot e inserisce il primo messaggio, senza inviare richieste.</p>
      <p className="small-label">Modalità LIVE. Nuova chat tra varianti; attendi ogni risposta. Ogni invio richiede consenso e un budget esplicito. {server.allowLive ? 'Il backend è pronto per le inferenze.' : 'Il backend non è ancora abilitato per le inferenze.'}</p>
      {needsNewChat && <div className="notice notice-warning" role="status">C’è già una prova nello storico corrente. Nel bot premi “Nuova chat” prima di applicare un preset, per non contaminare il confronto. Lo storico non viene cancellato.</div>}
      {disabled && <p role="status">Preparazione bloccata durante operazioni, run attivi o invii dall’esito incerto.</p>}
      {scenarios.loading && <p role="status">Lettura conversazioni DEVELOPMENT…</p>}
      {scenarios.error && <ErrorBox message={scenarios.error} retry={scenarios.reload} title="Conversazioni dei preset non disponibili" />}
    </section>
    {comparisonPresets.map((preset) => {
      const scenario = scenarios.data?.find((item) => item.id === preset.scenarioId && item.split === 'development');
      return <section className="panel view-panel" key={preset.id} aria-label={preset.title}>
        <h2>{preset.title}</h2>
        <details><summary>Dettagli prova · criterio e messaggi</summary>
          <p>{preset.method}</p>
          <p><strong>Atteso:</strong> {preset.expected}</p>
          {scenario ? <ol>{scenario.turns.map((turn, index) => <li key={index}>
            <p>{turn.message}</p>{turn.confirmAction && <p className="muted">Consenso manuale solo per la bozza sintetica.</p>}
          </li>)}</ol> : <p>Scenario DEVELOPMENT non disponibile: nessuna conversazione viene inventata.</p>}
        </details>
        <div className="comparison-variants">{preset.variants.map((variant) => {
          const settings = comparisonSettings(variant);
          let prepared: PreparedComparison | null = null;
          let reason: string | null = null;
          try { prepared = prepareComparison(preset, variant, server, scenarios.data ?? []); }
          catch (error) { reason = errorMessage(error); }
          return <article className="policy-card" key={variant.id}>
            <h3>{variant.label}</h3>
            <p className="small-label">{settings.modelProfileId} · prompt {settings.promptProfile} · ridondanza {variant.redundancy ? 'attiva' : 'spenta'}</p>
            <details><summary>Configurazione completa</summary>
              <ConfigurationSummary title={`Impostazioni · ${variant.label}`} settings={settings} server={server} />
              <pre>{JSON.stringify(settings, null, 2)}</pre>
            </details>
            {reason && <p className="inline-error">Non disponibile: {reason}</p>}
            <button type="button" className="button button-primary"
              disabled={disabled || needsNewChat || scenarios.loading || Boolean(scenarios.error) || !prepared}
              onClick={() => { if (prepared) onApply(prepared); }}>
              Prepara {variant.label} · LIVE
            </button>
          </article>;
        })}</div>
      </section>;
    })}
  </div>;
}
