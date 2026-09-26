// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { answerFromEvents, callsFromEvents, loadedServiceSkill, mergeEvents, parseRunEvent, protocolDetails, runEventsUrl } from '../../src/lib/events';
import { event, modelCall } from '../support/fixtures';

describe('eventi SSE registrati', () => {
  it('deduplica ID e sequenze e ordina senza duplicare answer.delta', () => {
    const first = event({ kind: 'answer.delta', message: 'Ciao ' });
    const second = event({ id: 'event-2', sequence: 2, kind: 'answer.delta', message: 'mondo' });
    const result = mergeEvents([first], [second, first, { ...second, id: 'duplicate-sequence' }]);
    expect(result).toHaveLength(2);
    expect(answerFromEvents(result)).toBe('Ciao mondo');
  });

  it('accetta il solo run richiesto e non inventa eventi su JSON invalido', () => {
    expect(parseRunEvent(JSON.stringify(event()), 'run-1')).toEqual(event());
    expect(() => parseRunEvent(JSON.stringify(event()), 'other-run')).toThrow('altro run');
    expect(() => parseRunEvent('not JSON', 'run-1')).toThrow();
  });

  it('legge ModelCallRecord solo quando effettivamente presente', () => {
    const call = modelCall();
    expect(callsFromEvents([event({ kind: 'model.completed', data: call })], [])).toEqual([call]);
    expect(callsFromEvents([event({ kind: 'model.completed', data: { madeUp: 12 } })], [])).toEqual([]);
    expect(callsFromEvents([event({ kind: 'model.completed', data: call })], [{ ...call, status: 'recorded' }])[0]?.status).toBe('recorded');
  });

  it('HTTP e skill non diventano chiamate modello o agenti sintetici', () => {
    const request = event({ kind: 'protocol.request', agent: 'router', data: { protocol: 'HTTP', service: 'returns' } });
    const loaded = event({ kind: 'skill.loaded', agent: 'router', data: { provider: 'AgentSkillsProvider', arguments: { name: 'shop-returns' } } });
    expect(protocolDetails(request)).toEqual({ protocol: 'HTTP', service: 'returns' });
    expect(loadedServiceSkill(loaded)).toBe('shop-returns');
    expect(callsFromEvents([request, loaded], [])).toEqual([]);
    expect(protocolDetails(event({ data: request.data }))).toBeNull();
    expect(protocolDetails(event({ kind: 'protocol.request', data: { protocol: 'A2A' }, agent: 'returns' }))).toEqual({ protocol: 'A2A', service: null });
    expect(loadedServiceSkill(event({ kind: 'tool.called', data: loaded.data }))).toBeNull();
    expect(loadedServiceSkill(event({ kind: 'skill.loaded', data: { arguments: { name: 'shop-router' } } }))).toBeNull();
    expect(loadedServiceSkill(event({ kind: 'skill.loaded', message: 'shop-returns', data: null }))).toBeNull();
  });

  it('ricostruisce l’URL pubblico senza usare host interni', () => {
    expect(runEventsUrl('a2a', 'run/with?special')).toBe('/api/a2a/runs/run%2Fwith%3Fspecial/events');
  });
});
