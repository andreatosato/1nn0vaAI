// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { comparisonScenarios, configurationFor, liveConfigurationFor } from '../support/fixtures';
import { comparisonPresets, comparisonSettings, prepareComparison } from '../../src/lib/comparisonPresets';

describe('preset per confronti controllati', () => {
  it.each(['inline', 'skills', 'a2a'] as const)('%s prepara tutte le varianti con configurazioni complete e messaggi del backend', (technology) => {
    expect(comparisonPresets).toHaveLength(3);
    for (const preset of comparisonPresets) for (const variant of preset.variants) {
      const prepared = prepareComparison(preset, variant, liveConfigurationFor(technology), comparisonScenarios);
      expect(prepared.settings).toEqual({
        mode: 'live', modelProfileId: variant.model, agentModels: {}, promptProfile: variant.prompt,
        promptBlocks: { checklist: false, outputContract: false, examples: false, redundancy: variant.redundancy ?? false, conflictingStyle: false },
        historyStrategy: 'full', toolTransport: 'direct', confirmAction: false, maxOutputTokens: 1500, maxModelCalls: 24,
        approvedBudgetUsd: 0.1,
      });
      expect(prepared.message).toBe(comparisonScenarios.find((item) => item.id === preset.scenarioId)?.turns[0]?.message);
      prepared.settings.agentModels.router = 'modified';
      prepared.settings.promptBlocks.checklist = true;
      expect(comparisonSettings(variant).agentModels).toEqual({});
      expect(comparisonSettings(variant).promptBlocks.checklist).toBe(false);
    }
  });

  it('cambia una sola variabile tra varianti adiacenti', () => {
    for (const preset of comparisonPresets) {
      for (let i = 1; i < preset.variants.length; i++) {
        const previous = preset.variants[i - 1], current = preset.variants[i];
        if (!previous || !current) throw new Error('Variante mancante');
        const left = comparisonSettings(previous), right = comparisonSettings(current);
        const changes = (Object.keys(left) as (keyof typeof left)[]).filter((key) => JSON.stringify(left[key]) !== JSON.stringify(right[key]));
        expect(changes).toEqual([preset.id === 'model' ? 'modelProfileId' : i === 1 ? 'promptProfile' : 'promptBlocks']);
      }
    }
  });

  it('rifiuta scenari mancanti, vuoti o holdout senza fallback', () => {
    for (const preset of comparisonPresets) for (const variant of preset.variants) {
      for (const scenarios of [[], comparisonScenarios.map((item) => ({ ...item, split: 'holdout' })),
        comparisonScenarios.map((item) => ({ ...item, turns: [] }))]) {
        expect(() => prepareComparison(preset, variant, configurationFor('inline'), scenarios)).toThrow('Scenario DEVELOPMENT');
      }
    }
  });

  it('non inventa modelli e rispetta disponibilità e abilitazione LIVE', () => {
    for (const preset of comparisonPresets) for (const variant of preset.variants) {
      const server = configurationFor('a2a');
      expect(() => prepareComparison(preset, variant, { ...server, allowLive: true, models: [] }, comparisonScenarios)).toThrow('Profilo modello non restituito');
      expect(() => prepareComparison(preset, variant, server, comparisonScenarios)).toThrow('LIVE non è pronto');
      expect(() => prepareComparison(preset, variant, { ...server, allowLive: true }, comparisonScenarios)).toThrow('non è configurato');
      const ready = { ...server, allowLive: true, models: server.models.map((model) => ({ ...model, configured: true })) };
      expect(() => prepareComparison(preset, variant, ready, comparisonScenarios)).toThrow('LIVE non pronto');
      expect(prepareComparison(preset, variant, liveConfigurationFor('a2a'), comparisonScenarios).settings)
        .toMatchObject({ mode: 'live', approvedBudgetUsd: 0.1 });
    }
  });
});
