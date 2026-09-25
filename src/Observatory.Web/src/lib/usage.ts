import type { ModelCallRecord } from '../contracts';

type Metric = 'inputTokens' | 'cachedInputTokens' | 'cacheWriteTokens' | 'outputTokens' | 'reasoningTokens' | 'estimatedCostUsd';

export interface ModelUsage extends Record<Metric, number | null> {
  key: string;
  modelProfileId: string;
  modelId: string | null;
  deployment: string | null;
  mode: string;
  agents: string[];
  callCount: number;
  pricedCallCount: number;
  totalTokens: number | null;
}

export interface AgentUsage {
  key: string;
  agent: string;
  mode: string;
  totalTokens: number | null;
  estimatedCostUsd: number | null;
}

function hasPricedCost(call: ModelCallRecord): boolean {
  return call.mode === 'live' && call.estimatedCostUsd != null
    && (call.costStatus === 'priced' || call.costStatus === 'estimated');
}

function summarizeCalls(calls: readonly ModelCallRecord[]) {
  const mode = calls.length > 0 && calls.every((call) => call.mode === 'live') ? 'live' : 'archive';
  const tokensAvailable = calls.length > 0 && calls.every((call) =>
    call.mode === 'live' && call.inputTokens != null && call.outputTokens != null);
  const costAvailable = calls.length > 0 && calls.every(hasPricedCost);
  return {
    mode,
    totalTokens: tokensAvailable
      ? calls.reduce((total, call) => total + (call.inputTokens ?? 0) + (call.outputTokens ?? 0), 0)
      : null,
    estimatedCostUsd: costAvailable
      ? calls.reduce((total, call) => total + (call.estimatedCostUsd ?? 0), 0)
      : null,
  };
}

export function summarizeAgentUsage(calls: readonly ModelCallRecord[]): AgentUsage[] {
  const groups = new Map<string, ModelCallRecord[]>();
  for (const call of calls) {
    const key = call.agent.trim().toLocaleLowerCase();
    const group = groups.get(key);
    if (group) group.push(call);
    else groups.set(key, [call]);
  }
  return [...groups].map(([key, group]) => ({
    key,
    agent: group[0]?.agent ?? key,
    ...summarizeCalls(group),
  }));
}

export function summarizeConversationUsage(calls: readonly ModelCallRecord[]) {
  return summarizeCalls(calls);
}

export function summarizeModelUsage(calls: readonly ModelCallRecord[]): ModelUsage[] {
  const groups = new Map<string, [ModelCallRecord, ...ModelCallRecord[]]>();
  for (const call of calls) {
    const key = JSON.stringify([call.mode, call.modelProfileId, call.modelId ?? null, call.deployment ?? null]);
    const group = groups.get(key);
    if (group) group.push(call);
    else groups.set(key, [call]);
  }
  return [...groups].map(([key, group]) => {
    const first = group[0];
    const sum = (metric: Metric): number | null => {
      if (first.mode !== 'live' || group.some((call) =>
        call[metric] == null || (metric === 'estimatedCostUsd' && !hasPricedCost(call)))) return null;
      return group.reduce((total, call) => total + (call[metric] ?? 0), 0);
    };
    const inputTokens = sum('inputTokens');
    const outputTokens = sum('outputTokens');
    return {
      key, modelProfileId: first.modelProfileId, modelId: first.modelId ?? null,
      deployment: first.deployment ?? null, mode: first.mode,
      agents: [...new Set(group.map((call) => call.agent))].sort(),
      callCount: group.length,
      pricedCallCount: group.filter(hasPricedCost).length,
      inputTokens, outputTokens,
      totalTokens: inputTokens === null || outputTokens === null ? null : inputTokens + outputTokens,
      cachedInputTokens: sum('cachedInputTokens'), cacheWriteTokens: sum('cacheWriteTokens'),
      reasoningTokens: sum('reasoningTokens'), estimatedCostUsd: sum('estimatedCostUsd'),
    };
  });
}
