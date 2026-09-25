import { z } from 'zod';
import {
  conversationSchema, demoConfigurationSchema, demoDataSchema, productSchema, promptPreviewSchema, runConfigurationSchema,
  replayCaptureSchema, runRecordSchema, scenarioSchema, turnAcceptedSchema,
} from '../contracts';
import type {
  DemoConfiguration, ExperimentRequest, ModelDefinition, RunConfiguration, RunEvent, SubmitTurnRequest, Technology,
} from '../contracts';
import { parseRunEvent } from './events';
import { newId } from './ids';
import { isRecord, redact, redactText } from './redaction';
import type { JsonValue } from './redaction';
import { readSseFrames } from './sse';

export interface HttpCapture {
  id: string;
  at: string;
  method: 'GET' | 'POST' | 'DELETE';
  path: string;
  request: JsonValue;
  response: JsonValue;
  status: number | null;
  error: string | null;
  responseContentType: string | null;
}

export class ApiError extends Error {
  constructor(message: string, readonly status: number | null) {
    super(message);
    this.name = 'ApiError';
  }
}

function responseError(value: unknown): string | null {
  if (!isRecord(value)) return null;
  for (const field of ['message', 'detail', 'error', 'title']) {
    if (typeof value[field] === 'string') return redactText(value[field]);
  }
  return null;
}

export function validatePreviewConfiguration(configuration: RunConfiguration, server: DemoConfiguration): RunConfiguration {
  const parsed = runConfigurationSchema.parse(configuration);
  if (parsed.unboundedExecution && !server.capabilities.allowUnboundedExecution) throw new Error('Esecuzione senza limiti non abilitata dal backend.');
  if (parsed.unboundedExecution && parsed.approvedBudgetUsd != null) throw new Error('Rimuovi il budget per eseguire senza limiti applicativi.');
  for (const agent of Object.keys(parsed.agentModels)) {
    if (!server.capabilities.agentNames.some((id) => id === agent)) throw new Error(`Agente non registrato per questa demo: ${agent}.`);
  }
  const selected = [parsed.modelProfileId, ...Object.values(parsed.agentModels)];
  for (const id of selected) {
    const model = server.models.find((item) => item.id === id);
    if (!model) throw new Error(`Profilo modello non restituito da /config: ${id}`);
  }
  if (!server.promptProfiles.includes(parsed.promptProfile)) throw new Error('Profilo prompt non abilitato dal backend.');
  if (!server.historyStrategies.includes(parsed.historyStrategy)) throw new Error('Strategia di history non abilitata dal backend.');
  for (const [id, enabled] of Object.entries(parsed.promptBlocks)) {
    if (enabled && !server.promptBlocks.some((block) => block.id === id)) throw new Error(`Blocco prompt non abilitato dal backend: ${id}.`);
  }
  return parsed;
}

export function liveModelUnavailableReason(model: ModelDefinition, server: DemoConfiguration): string | null {
  if (!model.configured) return `Il modello ${model.name} non è configurato per LIVE.`;
  if (server.capabilities.modelCapabilities?.find((item) => item.modelProfileId === model.id)?.liveReady !== true) {
    return `LIVE non pronto per ${model.name}: disponibilità, tariffe o capacità non verificate dal backend.`;
  }
  return null;
}

export function validateConfiguration(configuration: RunConfiguration, server: DemoConfiguration): RunConfiguration {
  if (configuration.mode !== 'live') throw new Error("È supportata solo l'esecuzione LIVE.");
  if (!server.allowLive) throw new Error('LIVE non è pronto nel backend. Nessuna richiesta al provider è stata inviata.');
  const parsed = validatePreviewConfiguration(configuration, server);
  if (parsed.mode === 'live') {
    for (const id of [parsed.modelProfileId, ...Object.values(parsed.agentModels)]) {
      const model = server.models.find((item) => item.id === id);
      const reason = model && liveModelUnavailableReason(model, server);
      if (reason) throw new Error(reason);
    }
    if (!parsed.unboundedExecution && parsed.approvedBudgetUsd == null) throw new Error('Imposta un budget approvato in USD prima di inviare in LIVE.');
    if (!parsed.unboundedExecution && parsed.approvedBudgetUsd != null && server.capabilities.maxApprovedBudgetUsd != null && parsed.approvedBudgetUsd > server.capabilities.maxApprovedBudgetUsd) {
      throw new Error(`Il budget LIVE supera il limite del backend: ${server.capabilities.maxApprovedBudgetUsd} USD per run.`);
    }
  }
  return parsed;
}

export function buildTurnRequest(
  message: string, configuration: RunConfiguration, server: DemoConfiguration, idempotencyKey: string,
): SubmitTurnRequest {
  if (!message.trim()) throw new Error('Scrivi un messaggio prima di inviare.');
  if (!idempotencyKey.trim()) throw new Error('Chiave di idempotenza mancante.');
  return {
    message: message.trim(),
    idempotencyKey,
    configuration: validateConfiguration(configuration, server),
  };
}

export class ObservatoryApi {
  readonly base: string;
  constructor(readonly technology: Technology, private readonly capture?: (entry: HttpCapture) => void) {
    this.base = `/api/${technology}`;
  }

  private async request<T>(
    path: string, schema: z.ZodType<T>, method: 'GET' | 'POST' | 'DELETE' = 'GET',
    body?: unknown, signal?: AbortSignal,
    readResponse?: (response: Response) => Promise<unknown>,
    accept: 'application/json' | 'text/event-stream' = 'application/json',
  ): Promise<T> {
    const at = new Date().toISOString();
    const fullPath = `${this.base}${path}`;
    let value: unknown = null;
    let status: number | null = null;
    let failure: string | null = null;
    let responseContentType: string | null = null;
    const controller = new AbortController();
    const abort = () => controller.abort(signal?.reason);
    if (signal?.aborted) abort();
    else signal?.addEventListener('abort', abort, { once: true });
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, path === '/experiments' && method === 'POST' ? 300_000 : 30_000);
    try {
      const response = await fetch(fullPath, {
        method, credentials: 'same-origin',
        headers: body === undefined ? { Accept: accept } : {
          Accept: accept, 'Content-Type': 'application/json',
        },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
        signal: controller.signal,
      });
      status = response.status;
      responseContentType = response.headers.get('Content-Type');
      if (response.ok && readResponse) {
        value = await readResponse(response);
      } else {
        const text = await response.text();
        if (text.trim()) {
          try {
            value = JSON.parse(text);
          } catch {
            throw new ApiError(
              `Risposta non JSON da ${fullPath} (HTTP ${response.status}). Verifica il proxy e il backend.`, status,
            );
          }
        }
      }
      if (!response.ok) {
        throw new ApiError(responseError(value) ?? `Errore HTTP ${response.status} da ${fullPath}.`, status);
      }
      const parsed = schema.safeParse(value);
      if (!parsed.success) {
        const fields = parsed.error.issues.slice(0, 4).map((issue) => issue.path.join('.')).join(', ');
        throw new ApiError(`Contratto API non valido per ${fullPath}${fields ? `: ${fields}` : ''}.`, status);
      }
      return parsed.data;
    } catch (error) {
      if (timedOut && !signal?.aborted) {
        failure = `Tempo massimo di risposta superato per ${fullPath}. Nessun invio viene ripetuto automaticamente; verifica lo storico prima di ritentare una scrittura.`;
        throw new ApiError(failure, status);
      }
      failure = redactText(error instanceof Error ? error.message : 'Richiesta non riuscita.');
      throw error;
    } finally {
      clearTimeout(timeout);
      signal?.removeEventListener('abort', abort);
      if (!signal?.aborted) {
        this.capture?.({
          id: newId(), at, method, path: fullPath, status, responseContentType,
          request: redact(body), response: redact(value), error: failure,
        });
      }
    }
  }

  config = async (signal?: AbortSignal) => {
    const configuration = await this.request('/config', demoConfigurationSchema, 'GET', undefined, signal);
    if (configuration.technology !== this.technology) {
      throw new ApiError(`Proxy errato: la demo ${this.technology} ha ricevuto la configurazione ${configuration.technology}.`, null);
    }
    return configuration;
  };
  previewPrompt = async (configuration: RunConfiguration, server: DemoConfiguration, signal?: AbortSignal) => {
    if (server.technology !== this.technology) throw new ApiError('Configurazione di una demo diversa dall’anteprima richiesta.', null);
    const body = validatePreviewConfiguration(configuration, server);
    const preview = await this.request('/prompts/preview', promptPreviewSchema, 'POST', body, signal);
    if (preview.technology !== this.technology) throw new ApiError('Proxy errato: anteprima di una tecnologia diversa.', null);
    const names = preview.agents.map((agent) => agent.agent);
    if (names.length !== server.capabilities.agentNames.length || new Set(names).size !== names.length
      || !server.capabilities.agentNames.every((agent) => names.includes(agent))) {
      throw new ApiError('L’anteprima non contiene esattamente gli agenti attivi dichiarati da /config.', null);
    }
    return preview;
  };
  products = (signal?: AbortSignal) => this.request('/products', z.array(productSchema), 'GET', undefined, signal);
  demoData = (signal?: AbortSignal) => this.request('/demo-data', demoDataSchema, 'GET', undefined, signal);
  scenarios = (signal?: AbortSignal) => this.request('/scenarios', z.array(scenarioSchema), 'GET', undefined, signal);
  conversations = (signal?: AbortSignal) => this.request('/conversations', z.array(conversationSchema), 'GET', undefined, signal);
  runs = (signal?: AbortSignal) => this.request('/runs', z.array(runRecordSchema), 'GET', undefined, signal);
  clearRunHistory = () => this.request('/runs', z.object({ deletedRuns: z.number().int().nonnegative() }), 'DELETE');
  conversation = (id: string, signal?: AbortSignal) => this.request(`/conversations/${encodeURIComponent(id)}`, conversationSchema, 'GET', undefined, signal);
  run = (id: string, signal?: AbortSignal) => this.request(`/runs/${encodeURIComponent(id)}`, runRecordSchema, 'GET', undefined, signal);
  createConversation = (title: string) => this.request('/conversations', conversationSchema, 'POST', { title });
  submitTurn = (id: string, request: SubmitTurnRequest) =>
    this.request(`/conversations/${encodeURIComponent(id)}/turns`, turnAcceptedSchema, 'POST', request);
  cancel = (id: string) => this.request(`/runs/${encodeURIComponent(id)}/cancel`, z.unknown(), 'POST');
  export = (id: string) => this.request(`/runs/${encodeURIComponent(id)}/export`, z.unknown());
  replay = (id: string, onEvent?: (event: RunEvent) => void) => this.request(
    `/runs/${encodeURIComponent(id)}/replay`, replayCaptureSchema, 'POST', undefined, undefined,
    async (response) => {
      const contentType = response.headers.get('Content-Type') ?? '';
      const originalRunId = response.headers.get('X-Original-Run-Id');
      if (!contentType.toLowerCase().startsWith('text/event-stream') || !response.body) {
        await response.body?.cancel();
        throw new ApiError('Il replay deve restituire uno stream SSE della registrazione originale.', response.status);
      }
      if (originalRunId !== id || response.headers.get('X-Replay-Only')?.toLowerCase() !== 'true') {
        await response.body.cancel();
        throw new ApiError('Header di provenienza del replay mancanti o incoerenti. La riproduzione non viene considerata valida.', response.status);
      }
      const events: RunEvent[] = [];
      for await (const frame of readSseFrames(response.body)) {
        if (frame.event !== 'message') throw new ApiError(`Evento SSE inatteso nel replay: ${frame.event}.`, response.status);
        const event = parseRunEvent(frame.data, originalRunId);
        events.push(event);
        onEvent?.(event);
      }
      return { contentType, originalRunId, replayOnly: true, events };
    },
    'text/event-stream',
  );
  experiment = (request: ExperimentRequest) => this.request('/experiments', z.unknown(), 'POST', request);
}
