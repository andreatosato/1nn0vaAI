// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RunRecord } from '../contracts';
import { ObservatoryApi } from './api';
import { observeRun } from './runMonitor';
import type { MonitorSnapshot } from './runMonitor';
import { event, FakeEventSource, jsonResponse, run } from '../test/fixtures';

describe('monitor run: reconnect e polling GET-only', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
  });

  it('non reinvia turni su disconnessione, deduplica al reconnect e rilegge il risultato finale', async () => {
    let current: RunRecord = run();
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => jsonResponse(current));
    vi.stubGlobal('fetch', fetchMock);
    const snapshots: MonitorSnapshot[] = [];
    const complete = vi.fn(async () => {});
    const monitor = observeRun(
      new ObservatoryApi('inline'),
      { runId: 'run-1', conversationId: 'conversation-1', watch: true },
      (snapshot) => snapshots.push(snapshot), complete,
    );
    await monitor.refresh();
    const first = FakeEventSource.instances[0];
    expect(first?.url).toBe('/api/inline/runs/run-1/events');
    const delta = event({ kind: 'answer.delta', message: 'Risposta ' });
    first?.emit(delta);
    first?.fail();
    await monitor.refresh();
    expect(first?.closed).toBe(true);
    expect(snapshots.at(-1)?.connection).toBe('polling');
    monitor.reconnect();
    FakeEventSource.instances[1]?.emit(delta);
    expect(snapshots.at(-1)?.events).toHaveLength(1);
    current = run({
      status: 'completed', completedAt: '2026-09-23T10:00:01Z',
      result: { answer: 'Risposta registrata', productIds: [83], sources: [] },
      events: [delta],
    });
    FakeEventSource.instances[1]?.emit(event({ id: 'final', sequence: 2, kind: 'run.completed' }));
    await monitor.refresh();
    expect(complete).toHaveBeenCalledTimes(1);
    expect(snapshots.at(-1)?.record?.result?.productIds).toEqual([83]);
    expect(snapshots.at(-1)?.connection).toBe('closed');
    expect(FakeEventSource.instances[1]?.closed).toBe(true);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    await monitor.refresh();
    expect(complete).toHaveBeenCalledTimes(1);
    monitor.dispose();
  });

  it('rende visibile un errore di GET e permette solo retry esplicito', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockRejectedValue(new Error('Rete non disponibile'));
    vi.stubGlobal('fetch', fetchMock);
    const snapshots: MonitorSnapshot[] = [];
    const monitor = observeRun(
      new ObservatoryApi('inline'),
      { runId: 'run-1', conversationId: 'conversation-1', watch: true },
      (snapshot) => snapshots.push(snapshot), async () => {},
    );
    await monitor.refresh();
    expect(snapshots.at(-1)?.error).toBe('Rete non disponibile');
    expect(snapshots.at(-1)?.connection).toBe('error');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    monitor.dispose();
  });
});
