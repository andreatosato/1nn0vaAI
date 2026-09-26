import type { PromptProfile, Service, Technology } from './contracts';

export const demoMetadata: Record<Technology, {
  title: string; subtitle: string; description: string; accent: string; number: string; detail: string; topology: string;
}> = {
  a2a: {
    title: 'Agent-to-Agent', subtitle: 'Agenti che si parlano', number: '01', accent: 'purple',
    description: 'Il router delega ai tre agenti remoti Catalog, Orders e Returns, ciascuno nel proprio servizio con API A2A.',
    detail: 'A2A · un router e tre agenti remoti',
    topology: 'Router → A2A → agenti remoti Catalog / Orders / Returns. Ogni agente esegue le proprie chiamate modello.',
  },
  skills: {
    title: 'Agent Skills', subtitle: 'Specialisti remoti come skill', number: '02', accent: 'blue',
    description: 'Un solo router scarica dai siti skill-* le istruzioni dei tre specialisti remoti e chiama lui le API business via HTTP, senza delegare.',
    detail: 'Skills · un router, tre siti skill remoti e tre servizi HTTP',
    topology: 'Router → skill catalog / orders / returns scaricate da skill-catalog / skill-orders / skill-returns → API business HTTP. Solo il router esegue un modello.',
  },
  inline: {
    title: 'Inline', subtitle: 'Istruzioni di integrazione nel prompt', number: '03', accent: 'teal',
    description: 'Un solo router usa istruzioni inline e strumenti per chiamare le API business HTTP di Catalog, Orders e Returns.',
    detail: 'Inline · un router e tre servizi HTTP, senza skill',
    topology: 'Router con istruzioni inline → API business HTTP Catalog / Orders / Returns. Nessuna skill caricata e nessuna delega A2A.',
  },
};

export const serviceMetadata: Record<Service, { name: string; description: string; skill: string }> = {
  catalog: { name: 'Catalog', description: 'Prodotti e fatti del catalogo pubblico.', skill: 'catalog' },
  orders: { name: 'Orders', description: 'Ordini, consegna, importo pagato e bozze di reso confermate.', skill: 'orders' },
  returns: { name: 'Returns', description: 'Policy e valutazione del reso; non crea bozze o rimborsi.', skill: 'returns' },
};

export const pages = [
  { id: 'chat', label: 'Configurazione', icon: 'book' },
  { id: 'examples', label: 'Confronti guidati', icon: 'play' },
  { id: 'trace', label: 'Traccia', icon: 'trace' },
  { id: 'inspector', label: 'Inspector', icon: 'code' },
  { id: 'usage', label: 'Token e costi', icon: 'chart' },
  { id: 'history', label: 'Storico ed esperimenti', icon: 'history' },
] as const;
export type Page = (typeof pages)[number]['id'];

export const promptExamples: Record<PromptProfile, { name: string; description: string; example: string }> = {
  bad: {
    name: 'BAD · ambiguo',
    description: 'Istruzioni poco vincolate: utile per osservare errori, non per raccomandarle.',
    example: '«Sii disponibile e risolvi il problema del cliente velocemente.»',
  },
  good: {
    name: 'GOOD · esplicito',
    description: 'Separa fatti e ipotesi; richiedi conferma prima di una bozza di reso.',
    example: '«Verifica ordine e policy. Distingui prezzo di listino e importo pagato. Non creare bozze senza conferma.»',
  },
  gpt5: {
    name: 'GPT-5 · profilo',
    description: 'Profilo di controllo didattico invariato, non ottimizzato per GPT-5. Le schede di consigli non lo riscrivono.',
    example: '«Usa gli strumenti per accertare i fatti. Restituisci una risposta verificabile con le fonti.»',
  },
  gpt6: {
    name: 'GPT-6 · profilo',
    description: 'Profilo di controllo didattico invariato, non ottimizzato per GPT-6. Confronta la richiesta registrata, non il nome.',
    example: '«Prima di decidere, verifica i vincoli applicabili. Chiedi i dettagli mancanti e non inventare un rimborso.»',
  },
};
