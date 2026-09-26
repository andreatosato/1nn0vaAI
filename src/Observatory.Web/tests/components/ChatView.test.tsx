import { useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { RunConfiguration, ScenarioDefinition } from '../../src/contracts';
import type { DemoState } from '../../src/hooks/useDemo';
import { configuration, conversation, scenario, settings } from '../support/fixtures';
import { ChatView } from '../../src/components/ChatView';

const main: ScenarioDefinition = { ...scenario, split: 'development' };
const standalone: ScenarioDefinition = {
  id: 'order-other-customer', name: 'Ordine di un altro cliente', group: 'orders', split: 'development',
  turns: [{ message: "Mostra l'ordine ORD-1001.", expectedIntent: 'orders', expectedFacts: ['non'], confirmAction: false }],
};
const holdout: ScenarioDefinition = {
  ...standalone, id: 'reserved', name: 'Riservato alla valutazione', split: 'holdout',
  turns: [{ ...standalone.turns[0]!, message: 'Domanda riservata', expectedFacts: ['Fatto riservato'] }],
};

function makeDemo(overrides: Partial<DemoState> = {}): DemoState {
  const remote = <T,>(data: T) => ({ data, error: null, loading: false, reload: vi.fn() });
  return {
    configuration: remote(configuration), products: remote([]),
    scenarios: remote([standalone, holdout, main]), conversations: remote([conversation]),
    conversation: {
      ...conversation,
      messages: [{ id: 'saved', role: 'assistant', text: 'Risposta già registrata', at: conversation.createdAt, productIds: [], sources: [] }],
    },
    monitor: { record: null, answer: '' }, tracked: null, pendingText: null,
    busy: false, running: false, actionBusy: false, failedSubmission: null,
    send: vi.fn().mockResolvedValue(false), newConversation: vi.fn(), selectConversation: vi.fn(),
    ...overrides,
  } as DemoState;
}

function Harness({ demo }: { demo: DemoState }) {
  const [draft, setDraft] = useState('La mia bozza');
  const [currentSettings, setSettings] = useState<RunConfiguration | null>({ ...settings, mode: 'live' });
  return <ChatView demo={demo} active settings={currentSettings} onSettingsChange={setSettings} draft={draft} setDraft={setDraft} />;
}

async function openGuide() {
  const user = userEvent.setup();
  await user.click(screen.getByText('Opzioni chat'));
  await user.click(screen.getByText('Percorso guidato · sei turni'));
  return user;
}

describe('domande DEVELOPMENT nella chat', () => {
  it('mette la risposta davanti ai dettagli tecnici e mantiene il consenso LIVE visibile', () => {
    render(<Harness demo={makeDemo()} />);
    expect(screen.getByText('Risposta già registrata')).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toBeVisible();
    expect(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ })).toBeVisible();
    expect(screen.getByText('Impostazioni del prossimo messaggio')).not.toBeVisible();
    expect(screen.getByLabelText('Conversazione')).not.toBeVisible();
  });

  it('porta alla nuova risposta senza spostare chi sta leggendo lo storico', async () => {
    const user = userEvent.setup();
    const demo = makeDemo();
    const { rerender } = render(<Harness demo={demo} />);
    const log = screen.getByRole('log');
    Object.defineProperties(log, {
      scrollHeight: { configurable: true, value: 1000 },
      clientHeight: { configurable: true, value: 200 },
    });
    const assistant = screen.getByText('Risposta già registrata').closest('li')!;
    Object.defineProperty(assistant, 'offsetTop', { configurable: true, value: 750 });
    const withAnswer = (answer: string): DemoState => ({
      ...demo, monitor: { ...demo.monitor, answer },
    });
    rerender(<Harness demo={withAnswer('Prima risposta via SSE')} />);
    const latest = screen.getByText('Prima risposta via SSE').closest('li')!;
    Object.defineProperty(latest, 'offsetTop', { configurable: true, value: 850 });
    rerender(<Harness demo={withAnswer('Risposta aggiornata via SSE')} />);
    expect(log.scrollTop).toBe(800);
    log.scrollTop = 100;
    fireEvent.scroll(log);
    rerender(<Harness demo={withAnswer('Risposta finale via SSE')} />);
    expect(log.scrollTop).toBe(100);
    await user.click(screen.getByRole('button', { name: "Vai all'ultima risposta" }));
    expect(log.scrollTop).toBe(800);
    expect(screen.queryByRole('button', { name: "Vai all'ultima risposta" })).not.toBeInTheDocument();
  });

  it('preferisce i sei turni e mostra aspettative, senza esporre holdout o avviare esecuzioni', async () => {
    const demo = makeDemo();
    render(<Harness demo={demo} />);
    await openGuide();
    expect(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' })).toHaveValue('main-six-turns');
    expect(screen.getByText('Aspettative, non misure:')).toBeInTheDocument();
    expect(screen.getAllByText('Dominio atteso:')).toHaveLength(6);
    expect(screen.getAllByText('Fatti/output attesi:')).toHaveLength(6);
    expect(screen.queryByText(/Riservato alla valutazione|Domanda riservata|Fatto riservato/)).not.toBeInTheDocument();
    expect(demo.send).not.toHaveBeenCalled();
    expect(demo.newConversation).not.toHaveBeenCalled();
  });

  it('cambiare scenario conserva bozza e storico; preparare una domanda cambia solo la bozza', async () => {
    const demo = makeDemo();
    render(<Harness demo={demo} />);
    const user = await openGuide();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' }), standalone.id);
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('La mia bozza');
    await user.click(screen.getByRole('button', { name: /Mostra l'ordine ORD-1001/ }));
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue(standalone.turns[0]!.message);
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveFocus();
    expect(within(screen.getByRole('log')).getByText('Risposta già registrata')).toBeInTheDocument();
    expect(demo.send).not.toHaveBeenCalled();
    expect(demo.newConversation).not.toHaveBeenCalled();
    expect(demo.selectConversation).not.toHaveBeenCalled();
  });

  it('anche la domanda di conferma azzera entrambi i consensi e non invia in LIVE', async () => {
    const demo = makeDemo();
    render(<Harness demo={demo} />);
    const user = await openGuide();
    const confirmation = screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ });
    const live = screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ });
    await user.click(confirmation);
    await user.click(live);
    expect(confirmation).toBeChecked();
    expect(live).toBeChecked();
    await user.click(screen.getByRole('button', { name: /Confermo solo una bozza sintetica/ }));
    expect(confirmation).not.toBeChecked();
    expect(live).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Invia' })).toBeDisabled();
    expect(screen.getByText(/Il turno scelto richiede una conferma/)).toBeInTheDocument();
    expect(demo.send).not.toHaveBeenCalled();
  });

  it.each(['busy', 'failedSubmission'] as const)('non sostituisce una bozza durante %s', async (state) => {
    const demo = makeDemo(state === 'busy' ? { busy: true } : {
      failedSubmission: { conversationId: conversation.id, request: { message: 'In attesa di retry', configuration: settings, idempotencyKey: 'retry-key' } },
    });
    render(<Harness demo={demo} />);
    await openGuide();
    expect(screen.getByRole('button', { name: /Vorrei una camicia/ })).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('La mia bozza');
    expect(demo.send).not.toHaveBeenCalled();
  });

  it('senza main usa un altro DEVELOPMENT; se rimangono solo holdout non mostra domande', async () => {
    const demo = makeDemo();
    demo.scenarios.data = [standalone, holdout];
    const { rerender } = render(<Harness demo={demo} />);
    await openGuide();
    expect(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' })).toHaveValue(standalone.id);
    rerender(<Harness demo={{ ...demo, scenarios: { ...demo.scenarios, data: [holdout] } }} />);
    expect(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' })).toBeDisabled();
    expect(screen.getByText('Questa API non ha restituito scenari DEVELOPMENT.')).toBeInTheDocument();
    expect(screen.queryByText(/Domanda riservata|Fatto riservato/)).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('La mia bozza');
  });
});
