import { agents, modelCallSchema, runEventSchema, services } from '../contracts';
import type { Agent, ModelCallRecord, RunEvent, Technology } from '../contracts';
import { isRecord } from './redaction';

export function mergeEvents(current: readonly RunEvent[], incoming: readonly RunEvent[]): RunEvent[] {
  const byId = new Map(current.map((event) => [event.id, event]));
  const sequences = new Set(current.filter((event) => event.sequence > 0).map((event) => `${event.runId}:${event.sequence}`));
  for (const event of incoming) {
    const key = `${event.runId}:${event.sequence}`;
    if (byId.has(event.id) || (event.sequence > 0 && sequences.has(key))) continue;
    byId.set(event.id, event);
    if (event.sequence > 0) sequences.add(key);
  }
  return [...byId.values()].sort((a, b) => a.sequence - b.sequence || Date.parse(a.at) - Date.parse(b.at));
}

export function callsFromEvents(events: readonly RunEvent[], recorded: readonly ModelCallRecord[]): ModelCallRecord[] {
  const calls = new Map<string, ModelCallRecord>();
  for (const event of events) {
    if (event.kind !== 'model.completed') continue;
    const parsed = modelCallSchema.safeParse(event.data);
    if (parsed.success) calls.set(parsed.data.id, parsed.data);
  }
  for (const call of recorded) calls.set(call.id, call);
  return [...calls.values()].sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt));
}

export function parseRunEvent(raw: string, runId: string): RunEvent {
  const value: unknown = JSON.parse(raw);
  const event = runEventSchema.parse(value);
  if (event.runId !== runId) throw new Error('Lo stream ha restituito un evento di un altro run.');
  return event;
}

export function runEventsUrl(technology: Technology, runId: string): string {
  // API-generated URLs may contain internal service names. Browser traffic stays same-origin.
  return `/api/${technology}/runs/${encodeURIComponent(runId)}/events`;
}

export function agentName(raw: string): Agent | null {
  return agents.find((agent) => agent.toLowerCase() === raw.toLowerCase()) ?? null;
}

export function protocolDetails(event: RunEvent): { protocol: string; service: string | null } | null {
  if (!['protocol.request', 'protocol.response'].includes(event.kind) || !isRecord(event.data)
    || typeof event.data.protocol !== 'string') return null;
  return {
    protocol: event.data.protocol,
    service: typeof event.data.service === 'string' ? event.data.service : null,
  };
}

export function loadedServiceSkill(event: RunEvent): string | null {
  if (event.kind !== 'skill.loaded' || !isRecord(event.data) || !isRecord(event.data.arguments)) return null;
  return Object.values(event.data.arguments).find((value): value is string =>
    typeof value === 'string' && services.some((service) => value === `shop-${service}`),
  ) ?? null;
}

export function answerFromEvents(events: readonly RunEvent[]): string {
  return events.filter((event) => event.kind === 'answer.delta').map((event) => event.message).join('');
}
