import { describe, expect, it } from 'vitest';
import { modelCall } from '../support/fixtures';
import { money } from '../../src/lib/format';
import { summarizeModelUsage } from '../../src/lib/usage';

const measured = {
  mode: 'live', usageSource: 'provider', costStatus: 'estimated',
  inputTokens: 100, cachedInputTokens: 20, cacheWriteTokens: 0,
  outputTokens: 40, reasoningTokens: 10, estimatedCostUsd: 0.001,
};

describe('consumi per modello', () => {
  it('riconosce lo stato priced del backend LIVE con i consumi provider verificati', () => {
    const result = summarizeModelUsage([
      modelCall({ ...measured, costStatus: 'priced', inputTokens: 2075, cachedInputTokens: 1920, outputTokens: 24, estimatedCostUsd: 0.00067375 }),
      modelCall({ ...measured, costStatus: 'priced', inputTokens: 2217, cachedInputTokens: 2048, outputTokens: 213, estimatedCostUsd: 0.00259725 }),
    ])[0];
    expect(result).toMatchObject({ inputTokens: 4292, outputTokens: 237, totalTokens: 4529, pricedCallCount: 2 });
    expect(result?.estimatedCostUsd).toBeCloseTo(0.003271, 10);
  });

  it('somma tutte le chiamate degli agenti senza ricontare cache e reasoning', () => {
    const result = summarizeModelUsage([
      modelCall({ ...measured, id: 'router-call', agent: 'router' }),
      modelCall({ ...measured, id: 'catalog-call', agent: 'catalog', inputTokens: 200 }),
    ]);
    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({
      agents: ['catalog', 'router'], callCount: 2, pricedCallCount: 2,
      inputTokens: 300, outputTokens: 80, totalTokens: 380,
      cachedInputTokens: 40, cacheWriteTokens: 0, reasoningTokens: 20, estimatedCostUsd: 0.002,
    });
  });

  it('separa profili, deployment e modelli effettivi', () => {
    const result = summarizeModelUsage([
      modelCall(measured), modelCall({ ...measured, modelProfileId: 'gpt6-astra' }),
      modelCall({ ...measured, deployment: 'second-deployment' }),
      modelCall({ ...measured, modelId: 'new-version' }), modelCall(),
    ]);
    expect(result).toHaveLength(4);
    expect(new Set(result.map((item) => item.key)).size).toBe(4);
  });

  it('non presenta somme parziali come totali, neppure dopo una chiamata fallita', () => {
    const result = summarizeModelUsage([
      modelCall(measured),
      modelCall({ ...measured, id: 'failed', status: 'failed', inputTokens: null, estimatedCostUsd: null, costStatus: 'unpriced' }),
    ])[0];
    expect(result).toMatchObject({
      inputTokens: null, outputTokens: 80, totalTokens: null, estimatedCostUsd: null,
      callCount: 2, pricedCallCount: 1,
    });
  });

  it('mantiene una metrica opzionale mancante distinta da zero', () => {
    expect(summarizeModelUsage([
      modelCall(measured), modelCall({ ...measured, cachedInputTokens: null, reasoningTokens: null }),
    ])[0]).toMatchObject({ cachedInputTokens: null, reasoningTokens: null, totalTokens: 280, estimatedCostUsd: 0.002 });
    expect(summarizeModelUsage([modelCall({ ...measured, inputTokens: 0, outputTokens: 0, estimatedCostUsd: 0 })])[0])
      .toMatchObject({ totalTokens: 0, estimatedCostUsd: 0, pricedCallCount: 1 });
  });

  it('non attribuisce token o costi LIVE a un record archiviato anche con valori presenti', () => {
    expect(summarizeModelUsage([modelCall({ ...measured, mode: 'archive' })])[0])
      .toMatchObject({ totalTokens: null, inputTokens: null, estimatedCostUsd: null, pricedCallCount: 0 });
    expect(summarizeModelUsage([])).toEqual([]);
  });

  it('non ricalcola i costi registrati dalle tariffe correnti', () => {
    expect(summarizeModelUsage([modelCall({ ...measured, estimatedCostUsd: 12.345678 })])[0]?.estimatedCostUsd).toBe(12.345678);
    expect(summarizeModelUsage([modelCall({ ...measured, costStatus: 'unpriced' })])[0]?.estimatedCostUsd).toBeNull();
  });
});

describe('precisione dei costi', () => {
  it('non arrotonda un costo positivo molto piccolo a zero', () => {
    expect(money(0.000000125)).toMatch(/^< /);
    expect(money(0)).not.toMatch(/^< /);
    expect(money(0.000001)).not.toMatch(/^< /);
    expect(money(null)).toBe('Non disponibile');
  });
});
