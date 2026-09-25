import type { RunEvent, RunRecord } from '../contracts';
import { modelCallSchema } from '../contracts';
import { ObservatoryApi } from './api';
import { mergeEvents, parseRunEvent, runEventsUrl } from './events';
import { isTerminal } from './format';
import { errorMessage, isAbort } from './redaction';

export interface TrackedRun {
  runId: string;
  conversationId: string;
  watch: boolean;
}

export interface MonitorSnapshot {
  record: RunRecord | null;
  events: RunEvent[];
  connection: 'idle' | 'connecting' | 'streaming' | 'polling' | 'closed' | 'error';
  notice: string | null;
  error: string | null;
}

export function emptyMonitor(): MonitorSnapshot {
  return { record: null, events: [], connection: 'idle', notice: null, error: null };
}

export function observeRun(
  api: ObservatoryApi,
  tracked: TrackedRun,
  onChange: (snapshot: MonitorSnapshot) => void,
  onTerminal: (record: RunRecord, signal: AbortSignal) => Promise<void>,
) {
  const controller = new AbortController();
  let source: EventSource | null = null;
  let polling: ReturnType<typeof setInterval> | null = null;
  let pendingRead: Promise<void> | null = null;
  let terminalNotified = false;
  let snapshot: MonitorSnapshot = emptyMonitor();

  const publish = (changes: Partial<MonitorSnapshot>) => {
    if (controller.signal.aborted) return;
    snapshot = { ...snapshot, ...changes };
    onChange(snapshot);
  };
  const stopPolling = () => {
    if (polling !== null) clearInterval(polling);
    polling = null;
  };
  const closeStream = () => {
    source?.close();
    source = null;
  };

  const refresh = (): Promise<void> => {
    if (controller.signal.aborted) return Promise.resolve();
    if (pendingRead) return pendingRead;
    pendingRead = (async () => {
      try {
        const record = await api.run(tracked.runId, controller.signal);
        if (controller.signal.aborted) return;
        if (record.id !== tracked.runId || record.conversationId !== tracked.conversationId) {
          throw new Error('Il backend ha restituito un run o una conversazione diversi da quelli richiesti.');
        }
        publish({ record, events: mergeEvents(snapshot.events, record.events), error: null });
        if (isTerminal(record.status)) {
          closeStream();
          stopPolling();
          publish({ connection: 'closed' });
          if (!terminalNotified) {
            terminalNotified = true;
            try {
              await onTerminal(record, controller.signal);
            } catch (error) {
              terminalNotified = false;
              throw error;
            }
          }
        }
      } catch (error) {
        if (controller.signal.aborted || isAbort(error)) return;
        stopPolling();
        publish({ error: errorMessage(error), connection: 'error' });
      } finally {
        pendingRead = null;
      }
    })();
    return pendingRead;
  };

  const startPolling = (notice: string) => {
    closeStream();
    publish({ connection: 'polling', notice });
    if (polling === null) polling = setInterval(() => { void refresh(); }, 2500);
    void refresh();
  };

  const reconnect = () => {
    if (controller.signal.aborted || isTerminal(snapshot.record?.status)) return;
    closeStream();
    stopPolling();
    publish({ connection: 'connecting', error: null, notice: null });
    try {
      const stream = new EventSource(runEventsUrl(api.technology, tracked.runId));
      source = stream;
      stream.onopen = () => {
        if (source === stream) publish({ connection: 'streaming' });
      };
      stream.onmessage = (message: MessageEvent<string>) => {
        if (source !== stream || controller.signal.aborted) return;
        try {
          const event = parseRunEvent(message.data, tracked.runId);
          publish({ events: mergeEvents(snapshot.events, [event]) });
          if (event.kind === 'model.completed' && !modelCallSchema.safeParse(event.data).success) {
            publish({ error: 'model.completed non contiene un ModelCallRecord valido. Il JSON originale è visibile nella traccia.' });
          }
          if (event.kind === 'run.completed' || event.kind === 'run.failed') {
            startPolling('Evento finale ricevuto: recupero il run e la conversazione dal backend.');
          }
        } catch (error) {
          startPolling('Evento SSE non valido. Leggo solo lo stato con GET; nessun messaggio viene reinviato.');
          publish({ error: errorMessage(error) });
        }
      };
      stream.onerror = () => {
        if (source !== stream || controller.signal.aborted) return;
        startPolling('Stream interrotto. Verifica stato ogni 2,5 s tramite GET; nessun turno viene reinviato e nessuna nuova chiamata al modello viene avviata.');
      };
    } catch (error) {
      startPolling('SSE non disponibile. Verifica dello stato tramite GET, senza reinvio del turno.');
      publish({ error: errorMessage(error) });
    }
  };

  if (tracked.watch) reconnect();
  void refresh();
  return {
    refresh,
    reconnect,
    dispose: () => {
      controller.abort();
      closeStream();
      stopPolling();
    },
  };
}
