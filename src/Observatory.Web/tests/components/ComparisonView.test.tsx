import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { comparisonScenarios, configuration, liveConfigurationFor } from '../support/fixtures';
import { ComparisonView } from '../../src/components/ComparisonView';

function props() {
  return { server: liveConfigurationFor('inline'),
    scenarios: { data: comparisonScenarios, loading: false, error: null, reload: vi.fn() },
    disabled: false, needsNewChat: false, onApply: vi.fn(), onOpenChat: vi.fn() };
}

describe('confronti preimpostati', () => {
  it('mostra tre prove e applica solo al click, senza rete o consensi', async () => {
    const user = userEvent.setup(), input = props(), fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    render(<ComparisonView {...input} />);
    expect(screen.getAllByRole('region', { name: /^[123]\./ })).toHaveLength(3);
    expect(screen.getAllByRole('button', { name: /^Prepara / })).toHaveLength(6);
    expect(input.onApply).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Prepara C · GPT-5 / GOOD + ridondanza · LIVE' }));
    expect(input.onApply).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({
      scenarioId: 'correction', message: 'Voglio restituire ORD-1042 per ripensamento.',
      settings: expect.objectContaining({ mode: 'live', agentModels: {}, confirmAction: false, promptBlocks: {
        checklist: false, outputContract: false, examples: false, redundancy: true, conflictingStyle: false,
      } }),
    }));
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([{ disabled: true }, { needsNewChat: true }])('blocca la preparazione con %j', (state) => {
    render(<ComparisonView {...props()} {...state} />);
    for (const button of screen.getAllByRole('button', { name: /^Prepara / })) expect(button).toBeDisabled();
  });

  it('mostra errori del backend, retry e varianti indisponibili senza sostituire il modello', async () => {
    const user = userEvent.setup(), input = props();
    render(<ComparisonView {...input} server={{ ...input.server, models: input.server.models.filter((item) => item.id !== 'gpt6-sol') }}
      scenarios={{ ...input.scenarios, error: 'Scenario server irraggiungibile' }} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Scenario server irraggiungibile');
    expect(screen.getByText(/Profilo modello non restituito.*gpt6-sol/)).toBeVisible();
    for (const button of screen.getAllByRole('button', { name: /^Prepara / })) expect(button).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Riprova' }));
    expect(input.scenarios.reload).toHaveBeenCalledTimes(1);
    expect(input.onApply).not.toHaveBeenCalled();
  });

  it('non consente di usare deployment non configurati nei preset LIVE', async () => {
    const input = props();
    render(<ComparisonView {...input} server={{ ...configuration, allowLive: true }} />);
    for (const button of screen.getAllByRole('button', { name: /^Prepara / })) expect(button).toBeDisabled();
    expect(input.onApply).not.toHaveBeenCalled();
  });

  it('non rende visibili messaggi holdout con lo stesso identificativo', () => {
    const input = props();
    render(<ComparisonView {...input} scenarios={{ ...input.scenarios, data: comparisonScenarios.map((item) => ({
      ...item, split: 'holdout', turns: [{ message: 'Test riservato segreto', expectedIntent: 'catalog', expectedFacts: [], confirmAction: false }],
    })) }} />);
    expect(screen.queryByText('Test riservato segreto')).not.toBeInTheDocument();
    for (const region of screen.getAllByRole('region', { name: /^[123]\./ })) {
      expect(within(region).getByText(/Scenario DEVELOPMENT non disponibile:/)).toBeInTheDocument();
    }
  });

  it('blocca soltanto il preset senza tariffe anche quando il deployment esiste', () => {
    const input = props();
    render(<ComparisonView {...input} server={{ ...input.server, capabilities: { ...input.server.capabilities,
      modelCapabilities: input.server.models.map((model) => ({ modelProfileId: model.id, liveReady: model.id !== 'gpt6-sol' })),
    } }} />);
    expect(screen.getByRole('button', { name: 'Prepara B · GPT-6 Sol / GOOD · LIVE' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Prepara Baseline GPT-5 / GOOD · LIVE' })).toBeEnabled();
    expect(screen.getByText(/LIVE non pronto per GPT-6 Sol/)).toBeVisible();
    expect(input.onApply).not.toHaveBeenCalled();
  });

  it('mantiene ogni preset LIVE-only quando il backend vieta le chiamate', () => {
    const input = props();
    render(<ComparisonView {...input} server={configuration} />);
    expect(screen.getByText(/Modalità LIVE/)).toBeInTheDocument();
    for (const button of screen.getAllByRole('button', { name: /^Prepara / })) expect(button).toBeDisabled();
    expect(input.onApply).not.toHaveBeenCalled();
  });
});
