import { useEffect, useMemo, useRef, useState } from 'react';
import type { RunRecord } from '../contracts';
import type { ObservatoryApi } from '../lib/api';
import { answerFromEvents, callsFromEvents } from '../lib/events';
import { emptyMonitor, observeRun } from '../lib/runMonitor';
import type { TrackedRun } from '../lib/runMonitor';

export function useRunMonitor(
  api: ObservatoryApi,
  tracked: TrackedRun | null,
  onTerminal: (record: RunRecord, signal: AbortSignal) => Promise<void>,
) {
  const [snapshot, setSnapshot] = useState(emptyMonitor);
  const observer = useRef<ReturnType<typeof observeRun> | null>(null);
  const runId = tracked?.runId;
  const conversationId = tracked?.conversationId;
  const watch = tracked?.watch;

  useEffect(() => {
    setSnapshot(emptyMonitor());
    if (!runId || !conversationId || watch === undefined) return;
    const current = observeRun(api, { runId, conversationId, watch }, setSnapshot, onTerminal);
    observer.current = current;
    return () => {
      current.dispose();
      if (observer.current === current) observer.current = null;
    };
  }, [api, runId, conversationId, watch, onTerminal]);

  const calls = useMemo(
    () => callsFromEvents(snapshot.events, snapshot.record?.calls ?? []),
    [snapshot.events, snapshot.record?.calls],
  );
  const answer = useMemo(() => answerFromEvents(snapshot.events), [snapshot.events]);
  return {
    ...snapshot, calls, answer,
    refresh: () => { void observer.current?.refresh(); },
    reconnect: () => observer.current?.reconnect(),
  };
}
