import { useCallback, useMemo, useRef, useState } from 'react';
import type {
  ConversationRecord, ReplayCapture, RunConfiguration, RunEvent, RunRecord,
  SubmitTurnRequest, Technology,
} from '../contracts';
import { buildTurnRequest, ObservatoryApi, validateConfiguration } from '../lib/api';
import type { HttpCapture } from '../lib/api';
import { isTerminal } from '../lib/format';
import { newId } from '../lib/ids';
import { errorMessage, safeJson } from '../lib/redaction';
import type { TrackedRun } from '../lib/runMonitor';
import { useRemote } from './useRemote';
import { useRunMonitor } from './useRunMonitor';

interface FailedSubmission {
  conversationId: string;
  request: SubmitTurnRequest;
}

export function useDemo(technology: Technology) {
  const [captures, setCaptures] = useState<HttpCapture[]>([]);
  const capture = useCallback((entry: HttpCapture) => setCaptures((previous) => [...previous.slice(-39), entry]), []);
  const api = useMemo(() => new ObservatoryApi(technology, capture), [technology, capture]);
  const configuration = useRemote(api.config);
  const products = useRemote(api.products);
  const scenarios = useRemote(api.scenarios);
  const conversations = useRemote(api.conversations);
  const runs = useRemote(api.runs);
  const [conversation, setConversation] = useState<ConversationRecord | null>(null);
  const [tracked, setTracked] = useState<TrackedRun | null>(null);
  const [pendingText, setPendingText] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionNotice, setActionNotice] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [failedSubmission, setFailedSubmission] = useState<FailedSubmission | null>(null);
  const [replayResult, setReplayResult] = useState<ReplayCapture | null>(null);
  const [replaySource, setReplaySource] = useState<RunRecord | null>(null);
  const [replayEvents, setReplayEvents] = useState<RunEvent[]>([]);
  const [replayStatus, setReplayStatus] = useState<'idle' | 'receiving' | 'completed' | 'failed'>('idle');
  const [historyVersion, setHistoryVersion] = useState(0);
  const lock = useRef(false);
  const reloadConversations = conversations.reload;
  const reloadRuns = runs.reload;

  const onTerminal = useCallback(async (record: RunRecord, signal: AbortSignal) => {
    const updated = await api.conversation(record.conversationId, signal);
    if (signal.aborted) return;
    setConversation(updated);
    setPendingText(null);
    reloadConversations();
    reloadRuns();
  }, [api, reloadConversations, reloadRuns]);
  const monitor = useRunMonitor(api, tracked, onTerminal);
  const running = tracked !== null && !isTerminal(monitor.record?.status);
  const busy = actionBusy || running;

  const clearReplay = () => {
    setReplayResult(null);
    setReplaySource(null);
    setReplayEvents([]);
    setReplayStatus('idle');
  };

  const perform = async (operation: () => Promise<void>) => {
    if (lock.current) return;
    lock.current = true;
    setActionBusy(true);
    setActionError(null);
    setActionNotice(null);
    try {
      await operation();
    } catch (error) {
      setActionError(errorMessage(error));
    } finally {
      lock.current = false;
      setActionBusy(false);
    }
  };

  const acceptTurn = async (conversationId: string, request: SubmitTurnRequest) => {
    try {
      const accepted = await api.submitTurn(conversationId, request);
      setFailedSubmission(null);
      setPendingText(request.message);
      clearReplay();
      setTracked({ runId: accepted.runId, conversationId: accepted.conversationId, watch: true });
    } catch (error) {
      setFailedSubmission({ conversationId, request });
      throw error;
    }
  };

  const send = async (message: string, settings: RunConfiguration, liveAuthorized: boolean) => {
    if (busy || failedSubmission) return false;
    let accepted = false;
    await perform(async () => {
      const server = configuration.data;
      if (!server || configuration.error) throw new Error('Carica una configurazione backend valida prima di inviare.');
      if (settings.mode === 'live' && !liveAuthorized) throw new Error('Autorizza esplicitamente la chiamata LIVE.');
      const request = buildTurnRequest(message, settings, server, newId());
      let target = conversation;
      if (!target) {
        target = await api.createConversation(message.trim().slice(0, 80) || 'Demo conferenza');
        setConversation(target);
        reloadConversations();
      }
      await acceptTurn(target.id, request);
      accepted = true;
    });
    return accepted;
  };

  const retrySubmission = () => perform(async () => {
    if (!failedSubmission || !configuration.data) return;
    validateConfiguration(failedSubmission.request.configuration, configuration.data);
    await acceptTurn(failedSubmission.conversationId, failedSubmission.request);
  });

  const newConversation = () => {
    if (busy) return Promise.resolve();
    return perform(async () => {
      const created = await api.createConversation('Demo conferenza · nuova conversazione');
      setConversation(created);
      setTracked(null);
      setPendingText(null);
      setFailedSubmission(null);
      clearReplay();
      reloadConversations();
    });
  };

  const selectConversation = (id: string) => {
    if (busy) return Promise.resolve();
    return perform(async () => {
      const selected = await api.conversation(id);
      const lastRunId = [...selected.messages].reverse().find((message) => message.runId)?.runId;
      const lastRun = lastRunId ? await api.run(lastRunId) : null;
      setConversation(selected);
      setFailedSubmission(null);
      setPendingText(null);
      clearReplay();
      setTracked(lastRun ? {
        runId: lastRun.id, conversationId: lastRun.conversationId, watch: !isTerminal(lastRun.status),
      } : null);
    });
  };

  const selectRun = (id: string) => {
    if (busy) return Promise.resolve();
    return perform(async () => {
      const selected = await api.run(id);
      const selectedConversation = await api.conversation(selected.conversationId);
      setConversation(selectedConversation);
      setFailedSubmission(null);
      setPendingText(null);
      clearReplay();
      setTracked({
        runId: selected.id, conversationId: selected.conversationId, watch: !isTerminal(selected.status),
      });
    });
  };

  const cancel = () => perform(async () => {
    if (!tracked || !running) return;
    await api.cancel(tracked.runId);
    setActionNotice('Cancellazione richiesta al backend. Lo stato definitivo è letto dal run.');
    monitor.refresh();
    reloadRuns();
  });

  const exportRun = (id: string) => perform(async () => {
    const result = await api.export(id);
    const blob = new Blob([safeJson(result)], { type: 'application/json' });
    const href = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = `observatory-${technology}-${id.replace(/[^a-zA-Z0-9_-]/g, '')}.json`;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(href), 1000);
    setActionNotice('Esportato il JSON registrato, con redazione difensiva delle credenziali.');
  });

  const replay = (id: string) => {
    if (busy) return Promise.resolve();
    return perform(async () => {
      clearReplay();
      const source = await api.run(id);
      if (source.id !== id || source.technology !== technology) throw new Error('La provenienza del run originale non corrisponde alla richiesta di replay.');
      if (!isTerminal(source.status)) throw new Error('Attendi la conclusione del run prima di riprodurre la registrazione.');
      setReplaySource(source);
      setReplayStatus('receiving');
      try {
        const result = await api.replay(id, (event) => setReplayEvents((current) => [...current, event]));
        setReplayResult(result);
        setReplayStatus('completed');
        setPendingText(null);
        setTracked({ runId: source.id, conversationId: source.conversationId, watch: false });
        setActionNotice('Replay SSE della registrazione originale completato. Nessun nuovo run e nessuna chiamata modello: la modalità e le misure restano quelle della registrazione.');
        reloadRuns();
        reloadConversations();
      } catch (error) {
        setReplayStatus('failed');
        throw error;
      }
    });
  };

  const clearRunHistory = () => {
    if (busy || failedSubmission) return Promise.resolve();
    return perform(async () => {
      const { deletedRuns } = await api.clearRunHistory();
      setTracked(null);
      setPendingText(null);
      setFailedSubmission(null);
      clearReplay();
      setConversation((current) => current ? {
        ...current,
        messages: current.messages.map((message) => message.runId === null ? message : { ...message, runId: null }),
      } : null);
      setHistoryVersion((current) => current + 1);
      reloadRuns();
      reloadConversations();
      setActionNotice(`Esecuzioni cancellate: ${deletedRuns}. I testi delle conversazioni restano disponibili.`);
    });
  };

  return {
    api, captures, configuration, products, scenarios, conversations, runs,
    conversation, tracked, pendingText, monitor, busy, running, actionBusy,
    actionError, actionNotice, failedSubmission, replayResult, replaySource, replayEvents, replayStatus, historyVersion,
    send, retrySubmission, newConversation, selectConversation, selectRun, cancel, exportRun, replay, clearRunHistory,
    discardSubmission: () => { setFailedSubmission(null); setActionError(null); },
    clearError: () => setActionError(null),
  };
}

export type DemoState = ReturnType<typeof useDemo>;
