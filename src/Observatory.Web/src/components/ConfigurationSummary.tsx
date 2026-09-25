import type { DemoConfiguration, RunConfiguration } from '../contracts';
import { promptBlockIds } from '../contracts';
import { agentName } from '../lib/events';
import { redactText } from '../lib/redaction';
import { promptExamples } from '../metadata';

export function ConfigurationSummary({ title, settings, server }: {
  title: string; settings: RunConfiguration; server: DemoConfiguration | null;
}) {
  const modelLabel = (id: string) => {
    const name = server?.models.find((model) => model.id === id)?.name;
    return redactText(name ? `${name} (${id})` : id);
  };
  const overrides = Object.entries(settings.agentModels);
  const enabledBlocks = promptBlockIds.filter((id) => settings.promptBlocks[id]).length;

  return <section className="configuration-summary" aria-label={title}>
    <strong>{title}</strong>
    <dl>
      <div><dt>Modello base</dt><dd>{modelLabel(settings.modelProfileId)}</dd></div>
      <div><dt>Profilo prompt</dt><dd>{promptExamples[settings.promptProfile].name}</dd></div>
      <div><dt>Blocchi opzionali</dt><dd>{enabledBlocks} di {promptBlockIds.length} attivi</dd></div>
      <div><dt>Memoria</dt><dd>{settings.historyStrategy === 'full' ? 'Completa' : 'Compatta'}</dd></div>
      {settings.unboundedExecution && <div><dt>Limiti applicativi</dt><dd>Nessun tetto di spesa, chiamate o durata; output predefinito del provider.</dd></div>}
      {overrides.length > 0 && <div className="summary-overrides"><dt>Override · prevalgono sul modello base</dt><dd><ul>
        {overrides.map(([agent, model]) => <li key={agent}>{agentName(agent) ?? redactText(agent)}: {modelLabel(model)}</li>)}
      </ul></dd></div>}
    </dl>
  </section>;
}
