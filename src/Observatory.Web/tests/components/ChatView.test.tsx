import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { RunConfiguration } from '../../src/contracts';
import type { DemoState } from '../../src/hooks/useDemo';
import { configuration, conversation, settings } from '../support/fixtures';
import { ChatView } from '../../src/components/ChatView';

function makeDemo(overrides: Partial<DemoState> = {}): DemoState {
  const remote = <T,>(data: T) => ({ data, error: null, loading: false, reload: vi.fn() });
  return {
    configuration: remote({ ...configuration, allowLive: true }), products: remote([]), scenarios: remote([]), conversations: remote([]), runs: remote([]),
    conversation: { ...conversation, messages: [
      { id: 'user', role: 'user', text: 'Domanda registrata', at: conversation.createdAt, productIds: [], sources: [] },
      { id: 'assistant', role: 'assistant', text: 'Risposta registrata', at: conversation.createdAt, productIds: [], sources: [] },
    ] },
    monitor: { record: null, answer: '' }, tracked: null, pendingText: null,
    busy: false, running: false, actionBusy: false, failedSubmission: null, actionError: null,
    send: vi.fn().mockResolvedValue(true), clearError: vi.fn(),
    ...overrides,
  } as DemoState;
}

function Harness({ demo }: { demo: DemoState }) {
  const [draft, setDraft] = useState('Domanda precompilata');
  const currentSettings: RunConfiguration = { ...settings, mode: 'live', unboundedExecution: true };
  return <ChatView demo={demo} active settings={currentSettings} draft={draft} setDraft={setDraft} />;
}

afterEach(() => vi.unstubAllGlobals());

describe('chat essenziale', () => {
  it('mostra solo domanda e risposta, senza controlli del laboratorio', () => {
    render(<Harness demo={makeDemo()} />);
    expect(screen.getAllByText('Domanda utente')).toHaveLength(2);
    expect(screen.getByText('Risposta bot')).toBeVisible();
    expect(screen.getByText('Domanda registrata')).toBeVisible();
    expect(screen.getByText('Risposta registrata')).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Domanda utente' })).toHaveValue('Domanda precompilata');
    expect(screen.queryByText('Opzioni chat')).not.toBeInTheDocument();
    expect(screen.queryByText('Configurazione')).not.toBeInTheDocument();
    expect(screen.queryByText('Percorso guidato · sei turni')).not.toBeInTheDocument();
  });

  it('richiede conferma prima di inviare una domanda LIVE', async () => {
    const user = userEvent.setup();
    const demo = makeDemo();
    vi.stubGlobal('confirm', vi.fn().mockReturnValue(true));
    render(<Harness demo={demo} />);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    expect(window.confirm).toHaveBeenCalledOnce();
    expect(demo.send).toHaveBeenCalledWith('Domanda precompilata', expect.anything(), true);
  });
});
