// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { defaultPromptBlocks, demoConfigurationSchema, runConfigurationSchema, runRecordSchema } from '../src/contracts';
import { configuration, run, settings } from './support/fixtures';

describe('compatibilità dei blocchi prompt', () => {
  it('legge run storici senza promptBlocks come tutti spenti senza alterare il profilo', () => {
    const legacy = JSON.parse(JSON.stringify(run())) as Record<string, unknown>;
    const legacyConfiguration = { ...settings, promptProfile: 'gpt6', promptBlocks: undefined };
    legacy.configuration = legacyConfiguration;
    const parsed = runRecordSchema.parse(legacy);
    expect(parsed.configuration.promptBlocks).toEqual(defaultPromptBlocks);
    expect(parsed.configuration.promptProfile).toBe('gpt6');
    expect(legacyConfiguration.promptBlocks).toBeUndefined();
  });

  it('non condivide le selezioni di default tra configurazioni legacy', () => {
    const first = runConfigurationSchema.parse({ ...settings, promptBlocks: undefined });
    const second = runConfigurationSchema.parse({ ...settings, promptBlocks: undefined });
    first.promptBlocks.checklist = true;
    expect(second.promptBlocks).toEqual(defaultPromptBlocks);
    expect(defaultPromptBlocks.checklist).toBe(false);
  });

  it.each([null, false, 'checklist', { ...defaultPromptBlocks, checklist: 'true' }, { ...defaultPromptBlocks, authorization: false }])
    ('rifiuta blocchi esplicitamente invalidi: %j', (promptBlocks) => {
      expect(() => runConfigurationSchema.parse({ ...settings, promptBlocks })).toThrow();
      expect(() => runRecordSchema.parse({ ...run(), configuration: { ...settings, promptBlocks } })).toThrow();
    });

  it('preserva i metadati server e rifiuta ID sconosciuti o duplicati', () => {
    expect(demoConfigurationSchema.parse(configuration).promptBlocks).toEqual(configuration.promptBlocks);
    expect(() => demoConfigurationSchema.parse({ ...configuration, promptBlocks: [{ id: 'unknown', label: 'X', description: '' }] })).toThrow();
    expect(() => demoConfigurationSchema.parse({ ...configuration, promptBlocks: [configuration.promptBlocks[0], configuration.promptBlocks[0]] })).toThrow();
  });
});
