import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { conversation, modelCall, run, settings } from '../support/fixtures';
import { money } from '../../src/lib/format';
import { UsageView } from '../../src/components/UsageView';

describe('riepilogo token e costi della conversazione', () => {
  it('somma token e prezzi di tutti i run della conversazione e li mostra per agente', () => {
    const firstRun = run({
      id: 'run-1',
      calls: [
        modelCall({ id: 'router-1', runId: 'run-1', mode: 'live', agent: 'router', inputTokens: 100, outputTokens: 30, estimatedCostUsd: 0.001, costStatus: 'priced' }),
        modelCall({ id: 'catalog-1', runId: 'run-1', mode: 'live', agent: 'catalog', inputTokens: 200, outputTokens: 50, estimatedCostUsd: 0.002, costStatus: 'estimated' }),
      ],
    });
    const currentRun = run({
      id: 'run-2',
      calls: [modelCall({ id: 'router-2', runId: 'run-2', mode: 'live', agent: 'Router', inputTokens: 10, outputTokens: 20, estimatedCostUsd: 0.0004, costStatus: 'priced' })],
    });
    render(<UsageView run={currentRun} calls={currentRun.calls} runs={[firstRun, currentRun]}
      conversationId={conversation.id} runsError={null} />);

    expect(screen.getByText('410')).toBeInTheDocument();
    expect(screen.getByText(money(0.0034).replace(/\s/g, ' '))).toBeInTheDocument();
    const table = within(screen.getByRole('table', { name: 'Riepilogo token e prezzo per agente' }));
    expect(table.getAllByRole('row')).toHaveLength(3);
    expect(table.getByRole('row', { name: /router/i })).toHaveTextContent('160');
    expect(table.getByRole('row', { name: /router/i })).toHaveTextContent(money(0.0014).replace(/\s/g, ' '));
    expect(table.getByRole('row', { name: /catalog/i })).toHaveTextContent('250');
    expect(table.getByRole('row', { name: /catalog/i })).toHaveTextContent(money(0.002).replace(/\s/g, ' '));
    expect(screen.queryByText(/profili e pricing/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Costo stimato API|input token|cache letta/i)).not.toBeInTheDocument();
  });

  it('mostra metriche non disponibili senza inventare token o prezzi', () => {
    const fixtureRun = run({ calls: [modelCall()] });
    render(<UsageView run={fixtureRun} calls={fixtureRun.calls} runs={[fixtureRun]}
      conversationId={conversation.id} runsError={null} />);
    expect(screen.getAllByText('Non disponibile')).toHaveLength(4);
    expect(screen.getByRole('row', { name: /router/i })).toBeInTheDocument();
  });

  it('mantiene i totali non disponibili quando una chiamata LIVE non ha usage o prezzo', () => {
    const liveRun = run({
      configuration: { ...settings, mode: 'live' },
      calls: [modelCall({ mode: 'live', inputTokens: 100, outputTokens: null, estimatedCostUsd: null, costStatus: 'unpriced' })],
    });
    render(<UsageView run={liveRun} calls={liveRun.calls} runs={[liveRun]}
      conversationId={conversation.id} runsError={null} />);
    expect(screen.getAllByText('Non disponibile')).toHaveLength(4);
  });

  it('mostra il caricamento vuoto finché non è selezionata una conversazione', () => {
    render(<UsageView run={null} calls={[]} runs={[]} conversationId={null} runsError={null} />);
    expect(screen.getByText('Nessuna conversazione selezionata')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
