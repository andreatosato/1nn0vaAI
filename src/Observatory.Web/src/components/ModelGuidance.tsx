import { useId, useRef, useState } from 'react';
import { modelGuidanceVerifiedAt, modelPromptGuidance } from '../modelGuidance';

export function ModelGuidance() {
  const id = useId();
  const [selected, setSelected] = useState(0);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  return <section className="model-guidance" aria-labelledby={`${id}-heading`}>
    <h3 id={`${id}-heading`}>Consigli dalle guide OpenAI</h3>
    <p><strong>Queste schede non cambiano il bot.</strong> Le quattro schede sono consultabili indipendentemente dal modello eseguibile scelto. Per cambiare il modello del prossimo messaggio usa il selettore <strong>Profilo modello</strong>, non questi tab. Non aggiungono modelli, non cambiano impostazioni e non ottimizzano automaticamente i profili di controllo GPT-5/GPT-6.</p>
    <div className="guidance-tabs" role="tablist" aria-label="Guide di prompting per famiglia">
      {modelPromptGuidance.map((guide, index) => <button
        key={guide.family} type="button" role="tab" id={`${id}-tab-${index}`} aria-controls={`${id}-panel-${index}`}
        aria-selected={selected === index} tabIndex={selected === index ? 0 : -1}
        ref={(element) => { tabs.current[index] = element; }}
        onClick={() => setSelected(index)}
        onKeyDown={(event) => {
          const next = event.key === 'ArrowRight' ? (index + 1) % modelPromptGuidance.length
            : event.key === 'ArrowLeft' ? (index + modelPromptGuidance.length - 1) % modelPromptGuidance.length
            : event.key === 'Home' ? 0 : event.key === 'End' ? modelPromptGuidance.length - 1 : null;
          if (next === null) return;
          event.preventDefault();
          setSelected(next);
          tabs.current[next]?.focus();
        }}
      >{guide.family}</button>)}
    </div>
    {modelPromptGuidance.map((guide, index) => <article className="guidance-card" key={guide.family}
      role="tabpanel" id={`${id}-panel-${index}`} aria-labelledby={`${id}-tab-${index}`} tabIndex={0} hidden={selected !== index}>
      <h4>{guide.family} · indicazioni documentali</h4>
      <p className="muted">{guide.scope}</p>
      <ul>{guide.tips.map((tip) => <li key={tip}>{tip}</li>)}</ul>
      <a href={guide.source.url} target="_blank" rel="noopener noreferrer">Fonte OpenAI: {guide.source.title} <span className="sr-only">(nuova scheda)</span></a>
    </article>)}
    <p className="small-label">Sintesi in italiano da fonti OpenAI verificate il {modelGuidanceVerifiedAt}. Confronta gli stessi scenari e verifica i risultati nelle tracce.</p>
  </section>;
}
