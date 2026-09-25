export const modelPromptGuidance = [
  {
    family: 'GPT-6',
    scope: 'La guida tratta la famiglia GPT-6, ma le raccomandazioni verificate qui riguardano soprattutto Astra. Non sono verificate per gli alias Sol/Luna della demo: non presumere comportamento o risultati equivalenti.',
    tips: [
      'Dichiara risultato atteso, margine di autonomia e quando un chiarimento cambia davvero l’esito.',
      'Controlla istruzioni e priorità delle skill: indicazioni poco chiare o in conflitto possono interrompere il lavoro.',
      'Specifica stile e struttura desiderati. Istruzioni estese ma coerenti possono essere utili: la sola lunghezza non dimostra un problema.',
    ],
    source: {
      title: 'Using GPT-6 · Prompting best practices',
      url: 'https://developers.openai.com/api/docs/guides/latest-model/gpt-6-astra.md#prompting-best-practices',
    },
  },
  {
    family: 'GPT-5.6',
    scope: 'Guida ufficiale per GPT-5.6 Sol e la famiglia GPT-5.6; nessuna promessa di miglioramento per questo scenario.',
    tips: [
      'Parti da risultato, prove necessarie, vincoli e criteri di completamento, senza prescrivere ogni passaggio.',
      'Rimuovi ripetizioni o esempi superflui un gruppo alla volta e ripeti gli stessi test. Conserva sicurezza, autorizzazioni, fonti e formato richiesto.',
      'Separa tono, collaborazione e lunghezza: un generico “sii breve” può togliere dettagli necessari.',
    ],
    source: {
      title: 'Prompting guidance for GPT-5.6 Sol',
      url: 'https://developers.openai.com/api/docs/guides/prompt-guidance-gpt-5p6',
    },
  },
  {
    family: 'GPT-5.4',
    scope: 'Consigli verificati nella guida d’uso GPT-5.4, sezione sulla verbosità; non sono una ricetta di ottimizzazione.',
    tips: [
      'Chiedi risposte focalizzate per richieste circoscritte e spiegazioni più estese quando servono documentazione o modifiche complesse.',
      'Indica nel prompt forma e dettaglio attesi: il prompt può guidare la verbosità della risposta.',
      'Distingui verbosità da ragionamento. Questi blocchi cambiano istruzioni testuali, non i parametri API di verbosity o reasoning.',
    ],
    source: {
      title: 'Using GPT-5.4 · Verbosity',
      url: 'https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.4#verbosity',
    },
  },
  {
    family: 'GPT-5',
    scope: 'Guida originale GPT-5; i suggerimenti vanno provati sui propri casi, non trasferiti automaticamente a tutte le versioni.',
    tips: [
      'Usa istruzioni dirette e strutturate, con confini dell’attività, condizioni di arresto e azioni che richiedono conferma.',
      'Elimina ambiguità e contraddizioni: seguire indicazioni incompatibili può causare lavoro inutile.',
      'Specifica tono e dettaglio per il contesto. La verbosità della risposta è distinta dal lavoro di ragionamento.',
    ],
    source: {
      title: 'GPT-5 prompting guide',
      url: 'https://developers.openai.com/cookbook/examples/gpt-5/gpt-5_prompting_guide',
    },
  },
] as const;

export const modelGuidanceVerifiedAt = '24 settembre 2026';
