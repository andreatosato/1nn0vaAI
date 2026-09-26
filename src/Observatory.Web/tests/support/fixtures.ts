import type {
  ConversationRecord, DemoConfiguration, DemoData, ModelCallRecord, Product,
  PromptPreview, RunConfiguration, RunEvent, RunRecord, ScenarioDefinition, Technology,
} from '../../src/contracts';
import { defaultPromptBlocks } from '../../src/contracts';

export const configuration: DemoConfiguration = {
  technology: 'inline', defaultMode: 'live', allowLive: false,
  models: ([
    ['gpt5', 'GPT-5', 'GPT-5', 'gpt-5'],
    ['gpt6-astra', 'GPT-6 Astra', 'GPT-6', 'gpt-6-astra'],
    ['gpt6-sol', 'GPT-6 Sol', 'GPT-6', 'gpt-6-sol'],
    ['gpt6-luna', 'GPT-6 Luna', 'GPT-6', 'gpt-6-luna'],
  ] as const).map(([id, name, family, modelId]) => ({
    id, name, family, modelId,
    configured: false,
    pricing: {
      inputPerMillion: null, cachedInputPerMillion: null, outputPerMillion: null,
      cacheWriteSurchargePerMillion: null, currency: 'USD', version: 'unconfigured',
    },
  })),
  promptProfiles: ['bad', 'good', 'gpt5', 'gpt6'],
  promptBlocks: [
    { id: 'checklist', label: 'Checklist di verifica', description: 'Controlli sui fatti e sulle informazioni mancanti.' },
    { id: 'outputContract', label: 'Contratto della risposta', description: 'Formato esplicito della risposta e delle fonti.' },
    { id: 'examples', label: 'Esempi di risposta', description: 'Esempi coerenti con i vincoli del dominio.' },
    { id: 'redundancy', label: 'Stile ridondante', description: 'Ripetizioni stilistiche controllate, senza cambiare autorizzazioni o fonti.' },
    { id: 'conflictingStyle', label: 'Stile contraddittorio', description: 'Indicazioni di stile incompatibili, senza modificare le regole di sicurezza.' },
  ],
  historyStrategies: ['full', 'compact'],
  capabilities: {
    agentNames: ['router'], serviceNames: ['catalog', 'orders', 'returns'],
    businessApi: true, executionTopology: 'router-http',
  },
  asOf: '2026-09-23T10:00:00Z',
  dataNotice: 'Catalogo pubblico DummyJSON; ordini e policy sintetici. Immagini solo nella UI.',
};

export function configurationFor(technology: Technology): DemoConfiguration {
  return {
    ...configuration, technology,
    capabilities: {
      ...configuration.capabilities,
      agentNames: technology === 'a2a' ? ['router', 'catalog', 'orders', 'returns'] : ['router'],
      businessApi: technology !== 'a2a',
      executionTopology: technology === 'a2a' ? 'router-a2a' : technology === 'skills' ? 'router-skills-http' : 'router-http',
    },
  };
}

export function liveConfigurationFor(technology: Technology): DemoConfiguration {
  const server = configurationFor(technology);
  return { ...server, allowLive: true, defaultMode: 'live',
    capabilities: { ...server.capabilities, maxApprovedBudgetUsd: 0.1,
      modelCapabilities: server.models.map((model) => ({ modelProfileId: model.id, liveReady: true })) },
    models: server.models.map((model) => ({
    ...model, configured: true, pricing: { ...model.pricing,
      inputPerMillion: 1, cachedInputPerMillion: 0.1, outputPerMillion: 2, version: 'test-only',
    },
  })) };
}

export const settings: RunConfiguration = {
  mode: 'live', modelProfileId: 'gpt5', agentModels: {}, promptProfile: 'good',
  promptBlocks: { ...defaultPromptBlocks },
  historyStrategy: 'full', toolTransport: 'direct', confirmAction: false,
  maxOutputTokens: 1500, maxModelCalls: 24,
};

export function promptPreview(technology: Technology = 'inline', instructions = 'Istruzioni di test.\nNon autorizzare azioni senza conferma.'): PromptPreview {
  return {
    technology,
    agents: configurationFor(technology).capabilities.agentNames.map((agent) => ({
      agent, instructions, characterCount: instructions.length,
    })),
    notice: 'Anteprima fixture: nessuna chiamata modello, tool o creazione di stato.',
  };
}

export const product: Product = {
  id: 83, title: 'Blue & Black Check Shirt', description: 'Camicia del catalogo pubblico.',
  category: 'mens-shirts', price: 29.99, currency: 'USD', stock: 12,
  brand: 'Test catalog', sku: 'TEST-83',
  thumbnail: 'https://cdn.dummyjson.com/product-images/mens-shirts/blue-&-black-check-shirt/thumbnail.webp',
  images: ['https://cdn.dummyjson.com/product-images/mens-shirts/blue-&-black-check-shirt/1.webp'],
  tags: ['shirt'],
};

export const demoData: DemoData = {
  asOf: '2026-09-23T10:00:00Z', customerId: 'CUST-DEMO-01',
  notice: 'Fixture di test sintetica per il solo ispettore dati.',
  orders: [
    { id: 'ORD-1042', customerId: 'CUST-DEMO-01', productId: 83, productTitle: product.title,
      listPrice: 29.99, amountPaid: 19.99, currency: 'USD', outlet: true,
      deliveredAt: '2026-09-05', status: 'delivered', trackingCode: 'TRACK-1042' },
    { id: 'ORD-1001', customerId: 'CUST-DEMO-02', productId: 84, productTitle: 'Altro prodotto sintetico',
      listPrice: 45, amountPaid: 40, currency: 'USD', outlet: false,
      deliveredAt: '2026-09-07', status: 'delivered', trackingCode: 'TRACK-1001' },
  ],
  policies: [
    { id: 'TEST-DEFECT', title: 'Difetto outlet', text: 'Eccezione sintetica per difetto: 60 giorni.', priority: 1, version: 'test-v1' },
    { id: 'TEST-REMORSE', title: 'Ripensamento outlet', text: 'Ripensamento sintetico entro 14 giorni.', priority: 2, version: 'test-v1' },
  ],
};

export const scenario: ScenarioDefinition = {
  id: 'main-six-turns', name: 'Sei turni: ordine e reso', group: 'conference', split: 'development',
  turns: [
    { message: 'Vorrei una camicia.', expectedIntent: 'catalog', expectedFacts: ['83'], confirmAction: false },
    { message: 'Mostrami il prodotto 83.', expectedIntent: 'catalog', expectedFacts: ['29.99'], confirmAction: false },
    { message: 'Verifica ORD-1042.', expectedIntent: 'orders', expectedFacts: ['19.99'], confirmAction: false },
    { message: 'Voglio restituire la camicia outlet.', expectedIntent: 'returns', expectedFacts: ['18'], confirmAction: false },
    { message: 'La camicia ha un difetto.', expectedIntent: 'returns', expectedFacts: ['defect'], confirmAction: false },
    { message: 'Confermo solo una bozza sintetica.', expectedIntent: 'returns', expectedFacts: ['draft'], confirmAction: true },
  ],
};

export const comparisonScenarios: ScenarioDefinition[] = [
  scenario,
  { id: 'correction', name: 'Il chiarimento cambia la decisione', group: 'multi-turn', split: 'development',
    turns: [
      { message: 'Voglio restituire ORD-1042 per ripensamento.', expectedIntent: 'returns', expectedFacts: ['14'], confirmAction: false },
      { message: 'Correggo: era difettoso, non e semplice ripensamento.', expectedIntent: 'returns', expectedFacts: ['60'], confirmAction: false },
    ] },
  { id: 'catalog-shirts', name: 'Camicie disponibili', group: 'catalog', split: 'development',
    turns: [{ message: 'Consigliami una camicia sotto i 40 dollari.', expectedIntent: 'catalog', expectedFacts: ['USD'], confirmAction: false }] },
];

export const conversation: ConversationRecord = {
  id: 'conversation-1', technology: 'inline', title: 'Conversazione di test',
  createdAt: '2026-09-23T10:00:00Z', messages: [],
};

export function event(overrides: Partial<RunEvent> = {}): RunEvent {
  return {
    id: 'event-1', sequence: 1, runId: 'run-1', at: '2026-09-23T10:00:00.010Z',
    kind: 'run.started', agent: 'system', message: 'Run avviato', data: null, ...overrides,
  };
}

export function modelCall(overrides: Partial<ModelCallRecord> = {}): ModelCallRecord {
  return {
    id: 'call-1', runId: 'run-1', agent: 'router', modelProfileId: 'gpt5', mode: 'live',
    modelId: 'gpt-5', startedAt: '2026-09-23T10:00:00.020Z', durationMs: 20,
    inputTokens: null, cachedInputTokens: null, cacheWriteTokens: null,
    outputTokens: null, reasoningTokens: null, usageSource: 'unavailable',
    estimatedCostUsd: null, costStatus: 'unpriced', captureKind: 'logical', status: 'completed',
    request: { messages: [{ role: 'user', content: 'Vorrei una camicia.' }] },
    response: { content: 'Ecco la camicia.' }, rawUsage: null, attempts: [], ...overrides,
  };
}

export function run(overrides: Partial<RunRecord> = {}): RunRecord {
  return {
    id: 'run-1', conversationId: conversation.id, technology: 'inline', status: 'running',
    message: 'Vorrei una camicia.', configuration: settings,
    startedAt: '2026-09-23T10:00:00Z', completedAt: null, durationMs: null,
    timeToFirstAnswerMs: null, result: null, error: null, events: [], calls: [],
    inputTokens: null, outputTokens: null, estimatedCostUsd: null, costStatus: 'unpriced',
    ...overrides,
  };
}

export class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly url: string;
  closed = false;
  onopen: ((event: Event) => void) | null = null;
  onmessage: ((event: MessageEvent<string>) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }
  close() { this.closed = true; }
  open() { this.onopen?.(new Event('open')); }
  emit(value: RunEvent) {
    this.onmessage?.(new MessageEvent<string>('message', { data: JSON.stringify(value) }));
  }
  fail() { this.onerror?.(new Event('error')); }
}

export function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
}

export function pathOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
}
