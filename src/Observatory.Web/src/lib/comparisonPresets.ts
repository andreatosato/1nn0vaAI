import { defaultPromptBlocks } from '../contracts';
import type { DemoConfiguration, RunConfiguration, ScenarioDefinition } from '../contracts';
import { validateConfiguration } from './api';

interface ComparisonVariant {
  id: string;
  label: string;
  model: string;
  prompt: RunConfiguration['promptProfile'];
  redundancy?: boolean;
}

export interface ComparisonPreset {
  id: string;
  title: string;
  scenarioId: string;
  method: string;
  expected: string;
  variants: readonly ComparisonVariant[];
}

export const comparisonPresets: readonly ComparisonPreset[] = [
  {
    id: 'architecture', title: '1. Architetture: tutti i domini',
    scenarioId: 'main-six-turns',
    method: 'Ripeti questi sei turni in Inline, Skills e A2A, sempre da una nuova chat. Cambia solo architettura: modello, prompt, modalità, limiti e messaggi restano uguali. In A2A tutti gli agenti ereditano GPT-5.',
    expected: 'Orders: consegna 2026-09-05. Returns: ripensamento negato (18 > 14 giorni), difetto ammesso entro 60 giorni. Catalog: camicie sotto 40 USD. Importo pagato: 19.99 USD, non 29.99. Ultimo turno: solo bozza sintetica con consenso manuale, mai rimborso reale.',
    variants: [{ id: 'baseline', label: 'Baseline GPT-5 / GOOD', model: 'gpt5', prompt: 'good' }],
  },
  {
    id: 'prompt', title: '2. Prompt: ambiguo, chiaro o prolisso',
    scenarioId: 'correction',
    method: 'Resta nella stessa tecnologia e usa GPT-5. Confronta A con B cambiando solo il profilo prompt; poi B con C cambiando solo il blocco Stile ridondante. Usa una nuova chat per ogni variante e ripeti entrambi i messaggi.',
    expected: 'Prima risposta: ripensamento outlet negato. Dopo il chiarimento: rivalutare il difetto con limite di 60 giorni, mantenendo ORD-1042 dal contesto. Confronta correttezza, chiarezza, ripetizioni, token e costo; un prompt più lungo non è necessariamente migliore.',
    variants: [
      { id: 'bad', label: 'A · GPT-5 / BAD', model: 'gpt5', prompt: 'bad' },
      { id: 'good', label: 'B · GPT-5 / GOOD', model: 'gpt5', prompt: 'good' },
      { id: 'verbose', label: 'C · GPT-5 / GOOD + ridondanza', model: 'gpt5', prompt: 'good', redundancy: true },
    ],
  },
  {
    id: 'model', title: '3. Modelli: raccomandazione dal catalogo',
    scenarioId: 'catalog-shirts',
    method: 'Resta nella stessa tecnologia. Confronta GPT-5 e GPT-6 Sol con identico prompt GOOD, stesso messaggio e nuova chat per ogni prova. Gli override vengono rimossi: anche in A2A il modello selezionato vale per tutti gli agenti.',
    expected: 'Camicie realmente presenti nel catalogo, sotto 40 USD, con ID e prezzi verificabili; nessun prodotto inventato. Verifica Catalog nella traccia e confronta qualità, token, costo e durata.',
    variants: [
      { id: 'gpt5', label: 'A · GPT-5 / GOOD', model: 'gpt5', prompt: 'good' },
      { id: 'gpt6-sol', label: 'B · GPT-6 Sol / GOOD', model: 'gpt6-sol', prompt: 'good' },
    ],
  },
];

export function comparisonSettings(variant: ComparisonVariant): RunConfiguration {
  return {
    mode: 'live', modelProfileId: variant.model, agentModels: {}, promptProfile: variant.prompt,
    promptBlocks: { ...defaultPromptBlocks, redundancy: variant.redundancy ?? false },
    historyStrategy: 'full', toolTransport: 'direct', confirmAction: false,
    maxOutputTokens: 1500, maxModelCalls: 24, approvedBudgetUsd: 0.1,
  };
}

export interface PreparedComparison {
  label: string;
  scenarioId: string;
  message: string;
  settings: RunConfiguration;
}

export function prepareComparison(
  preset: ComparisonPreset, variant: ComparisonVariant,
  server: DemoConfiguration, scenarios: readonly ScenarioDefinition[],
): PreparedComparison {
  const scenario = scenarios.find((item) => item.id === preset.scenarioId && item.split === 'development');
  const message = scenario?.turns[0]?.message;
  if (!message?.trim()) throw new Error(`Scenario DEVELOPMENT ${preset.scenarioId} non disponibile o vuoto.`);
  const settings = validateConfiguration(comparisonSettings(variant), server);
  return {
    label: `${preset.title} · ${variant.label}`, scenarioId: preset.scenarioId, message,
    settings,
  };
}
