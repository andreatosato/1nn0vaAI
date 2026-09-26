import { useState } from 'react';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { defaultPromptBlocks, runConfigurationSchema } from '../contracts';
import type { DemoConfiguration, RunConfiguration } from '../contracts';
import { ObservatoryApi } from '../lib/api';
import { configuration, configurationFor, jsonResponse, promptPreview, settings } from '../test/fixtures';
import { ConfigurationPanel, initialSettings } from './ConfigurationPanel';
import { PromptLab } from './PromptLab';

function Harness({ api, server = configuration, initial = settings, disabled = false, withControls = false }: {
  api: ObservatoryApi; server?: DemoConfiguration; initial?: RunConfiguration; disabled?: boolean; withControls?: boolean;
}) {
  const [value, setValue] = useState(initial);
  const Editor = withControls ? ConfigurationPanel : PromptLab;
  return <>
    <Editor api={api} server={server} settings={value} onChange={setValue} disabled={disabled} />
    <output data-testid="settings">{JSON.stringify(value)}</output>
  </>;
}

function selection(): RunConfiguration {
  return runConfigurationSchema.parse(JSON.parse(screen.getByTestId('settings').textContent ?? '{}'));
}

function deferredResponse() {
  let resolve!: (response: Response) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<Response>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe('laboratorio prompt senza inferenza', () => {
  it('parte con cinque switch spenti per ogni profilo e non fa richieste su apertura o cambio blocchi', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    for (const profile of configuration.promptProfiles) {
      const initial = initialSettings({ ...configuration, promptProfiles: [profile] });
      expect(initial?.promptBlocks).toEqual(defaultPromptBlocks);
      expect(initial?.promptProfile).toBe(profile);
    }
    render(<Harness api={new ObservatoryApi('inline')} />);
    const blocks = screen.getByRole('group', { name: 'Blocchi opzionali del prompt' });
    expect(within(blocks).getAllByRole('checkbox')).toHaveLength(5);
    for (const block of configuration.promptBlocks) {
      const checkbox = within(blocks).getByRole('checkbox', { name: block.label });
      expect(checkbox).not.toBeChecked();
      expect(checkbox).toHaveAccessibleDescription(block.description);
      await user.click(checkbox);
      expect(checkbox).toBeChecked();
    }
    expect(selection().promptBlocks).toEqual({
      checklist: true, outputContract: true, examples: true, redundancy: true, conflictingStyle: true,
    });
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByText(/Lungo non significa cattivo/)).toBeInTheDocument();
  });

  it('i preset e il reset conservano profili, modelli, history, limiti e budget', async () => {
    const user = userEvent.setup();
    const initial: RunConfiguration = {
      ...settings, promptProfile: 'gpt5', modelProfileId: 'gpt6-sol', agentModels: { router: 'gpt6-luna' },
      historyStrategy: 'compact', maxOutputTokens: 2400, maxModelCalls: 12, approvedBudgetUsd: 0.4, confirmAction: true,
    };
    render(<Harness api={new ObservatoryApi('inline')} initial={initial} />);
    await user.click(screen.getByRole('button', { name: 'Dettagliato coerente' }));
    const detailed = { ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true };
    expect(selection()).toEqual({ ...initial, promptBlocks: detailed, confirmAction: false });
    await user.click(screen.getByRole('button', { name: 'Ridondante · stile' }));
    expect(selection().promptBlocks).toEqual({ ...detailed, redundancy: true });
    await user.click(screen.getByRole('button', { name: 'Stile in conflitto' }));
    expect(selection().promptBlocks).toEqual({ ...detailed, conflictingStyle: true });
    await user.click(screen.getByRole('button', { name: 'Base · tutti spenti' }));
    expect(selection()).toEqual({ ...initial, promptBlocks: defaultPromptBlocks, confirmAction: false });
    expect(initial.confirmAction).toBe(true);
  });

  it('mostra solo istruzioni e caratteri realmente restituiti, non una ricostruzione o HTML eseguibile', async () => {
    const user = userEvent.setup();
    const instructions = 'Istruzioni dal server.\n  Conserva questo rientro.\n<img src=x onerror="alert(1)">\nFine.';
    const result = promptPreview('inline', instructions);
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(result));
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<Harness api={new ObservatoryApi('inline')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByText(result.notice);
    expect(container.querySelector('.prompt-preview-agent pre')?.textContent).toBe(instructions);
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText(`Router · ${instructions.length.toLocaleString('it-IT')} caratteri`)).toBeInTheDocument();
    expect(screen.getByText(/Non è la richiesta completa né una cattura wire/)).toBeInTheDocument();
    expect(screen.getByText(/istruzioni e risorse native delle skill, history, strumenti e risultati/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Inspector delle richieste' })).toHaveAttribute('href', '#/inline/inspector');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/inline/prompts/preview', expect.objectContaining({ method: 'POST', body: JSON.stringify(settings) }));
  });

  it('rende consultabili le istruzioni dei quattro agenti A2A senza crearne chiamate', async () => {
    const user = userEvent.setup();
    const result = promptPreview('a2a');
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(result)));
    const { container } = render(<Harness api={new ObservatoryApi('a2a')} server={configurationFor('a2a')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByText(result.notice);
    expect(container.querySelectorAll('.prompt-preview-agent')).toHaveLength(4);
    for (const agent of ['Router', 'Catalog', 'Orders', 'Returns']) {
      expect(screen.getByText(new RegExp(`^${agent} · \\d+ caratteri$`))).toBeInTheDocument();
    }
    const orders = screen.getByText(/^Orders ·/).closest('details');
    expect(orders).not.toHaveAttribute('open');
    await user.click(screen.getByText(/^Orders ·/));
    expect(orders).toHaveAttribute('open');
  });

  it('gli errori non creano preview finte né retry automatici; il retry è esplicito', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(jsonResponse({ message: 'Preview non disponibile dal server' }, 503))
      .mockResolvedValueOnce(jsonResponse(promptPreview()));
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<Harness api={new ObservatoryApi('inline')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByRole('alert');
    expect(screen.getByText('Anteprima non disponibile')).toBeInTheDocument();
    expect(container.querySelector('.prompt-preview-agent')).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole('button', { name: 'Riprova' }));
    await screen.findByText(promptPreview().notice);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it.each(['risposta', 'errore'] as const)('ignora una %s obsoleta dopo cambio blocchi, anche se fetch ignora abort', async (late) => {
    const user = userEvent.setup();
    const first = deferredResponse();
    const second = deferredResponse();
    const fetchMock = vi.fn<typeof fetch>()
      .mockImplementationOnce(() => first.promise).mockImplementationOnce(() => second.promise);
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<Harness api={new ObservatoryApi('inline')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByText('Caricamento dell’anteprima dal backend…');
    const firstSignal = fetchMock.mock.calls[0]?.[1]?.signal;
    await user.click(screen.getByRole('checkbox', { name: 'Checklist di verifica' }));
    expect(firstSignal?.aborted).toBe(true);
    expect(screen.queryByText('Caricamento dell’anteprima dal backend…')).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await act(async () => { second.resolve(jsonResponse(promptPreview('inline', 'ANTEPRIMA CORRENTE'))); });
    await screen.findByText('ANTEPRIMA CORRENTE');
    await act(async () => {
      if (late === 'risposta') first.resolve(jsonResponse(promptPreview('inline', 'ANTEPRIMA OBSOLETA')));
      else first.reject(new Error('ERRORE OBSOLETO'));
    });
    expect(container.querySelector('.prompt-preview-agent pre')).toHaveTextContent('ANTEPRIMA CORRENTE');
    expect(screen.queryByText(/OBSOLET/)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('invalida il risultato dopo ogni modifica e non lo riutilizza tornando alla vecchia selezione', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(promptPreview('inline', 'PRIMA SELEZIONE')));
    vi.stubGlobal('fetch', fetchMock);
    render(<Harness api={new ObservatoryApi('inline')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByText('PRIMA SELEZIONE');
    await user.click(screen.getByRole('checkbox', { name: 'Checklist di verifica' }));
    expect(screen.queryByText('PRIMA SELEZIONE')).not.toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'Checklist di verifica' }));
    expect(screen.queryByText('PRIMA SELEZIONE')).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('modello e profilo prompt invalidano le preview pendenti senza richieste automatiche o risultati obsoleti', async () => {
    const user = userEvent.setup();
    const first = deferredResponse();
    const second = deferredResponse();
    const third = deferredResponse();
    const fetchMock = vi.fn<typeof fetch>().mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise).mockImplementationOnce(() => third.promise);
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<Harness api={new ObservatoryApi('inline')} initial={{ ...settings, promptProfile: 'bad' }} withControls />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Profilo modello' }), 'gpt6-astra');
    expect(fetchMock.mock.calls[0]?.[1]?.signal?.aborted).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Profilo prompt' }), 'good');
    expect(fetchMock.mock.calls[1]?.[1]?.signal?.aborted).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    const instructions = 'Istruzioni correnti in italiano: verifica i fatti e spiega ciò che manca.';
    await act(async () => { third.resolve(jsonResponse(promptPreview('inline', instructions))); });
    await screen.findByText(instructions);
    await act(async () => {
      first.resolve(jsonResponse(promptPreview('inline', 'TESTO OBSOLETO')));
      second.reject(new Error('ERRORE OBSOLETO'));
    });
    expect(container.querySelector('.prompt-preview-agent pre')?.textContent).toBe(instructions);
    expect(screen.queryByText(/OBSOLET/)).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(selection()).toMatchObject({ modelProfileId: 'gpt6-astra', promptProfile: 'good' });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    const configurations = fetchMock.mock.calls.map(([input, init]) => {
      expect(input).toBe('/api/inline/prompts/preview');
      return runConfigurationSchema.parse(JSON.parse(String(init?.body)));
    });
    expect(configurations.map(({ modelProfileId, promptProfile }) => ({ modelProfileId, promptProfile }))).toEqual([
      { modelProfileId: 'gpt5', promptProfile: 'bad' },
      { modelProfileId: 'gpt6-astra', promptProfile: 'bad' },
      { modelProfileId: 'gpt6-astra', promptProfile: 'good' },
    ]);
  });

  it.each(['annulla', 'unmount', 'run-in-corso'] as const)('annulla una preview pendente su %s', async (action) => {
    const user = userEvent.setup();
    const response = deferredResponse();
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(() => response.promise);
    vi.stubGlobal('fetch', fetchMock);
    const api = new ObservatoryApi('inline');
    const { rerender, unmount } = render(<Harness api={api} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    if (action === 'annulla') {
      await user.click(screen.getByRole('button', { name: 'Annulla anteprima' }));
      expect(screen.getByText('Anteprima annullata. Nessun modello avviato.')).toBeInTheDocument();
    } else if (action === 'unmount') unmount();
    else rerender(<Harness api={api} disabled />);
    expect(fetchMock.mock.calls[0]?.[1]?.signal?.aborted).toBe(true);
    await act(async () => { response.resolve(jsonResponse(promptPreview('inline', 'RISPOSTA DA IGNORARE'))); });
    expect(screen.queryByText('RISPOSTA DA IGNORARE')).not.toBeInTheDocument();
  });

  it('cambiando tecnologia annulla il trasporto e non mostra istruzioni dell’altra demo', async () => {
    const user = userEvent.setup();
    const response = deferredResponse();
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(() => response.promise);
    vi.stubGlobal('fetch', fetchMock);
    const { rerender } = render(<Harness api={new ObservatoryApi('inline')} />);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    rerender(<Harness api={new ObservatoryApi('skills')} server={configurationFor('skills')} />);
    expect(fetchMock.mock.calls[0]?.[1]?.signal?.aborted).toBe(true);
    await act(async () => { response.resolve(jsonResponse(promptPreview('inline', 'VECCHIA DEMO'))); });
    expect(screen.queryByText('VECCHIA DEMO')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Inspector delle richieste' })).toHaveAttribute('href', '#/skills/inspector');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('disabilita switch, preset e richieste durante un invio o run, ma non confonde la lettura dei consigli con un’esecuzione', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    render(<Harness api={new ObservatoryApi('inline')} disabled />);
    for (const checkbox of screen.getAllByRole('checkbox')) expect(checkbox).toBeDisabled();
    for (const button of screen.getAllByRole('button')) expect(button).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: 'Checklist di verifica' }));
    await user.click(screen.getByRole('tab', { name: 'GPT-5.6' }));
    expect(screen.getByRole('tabpanel', { name: 'GPT-5.6' })).toBeInTheDocument();
    expect(selection().promptBlocks).toEqual(defaultPromptBlocks);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('leggere le quattro schede non cambia il modello eseguibile né aggiunge GPT-5.6 o GPT-5.4 al registro', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    render(<ConfigurationPanel api={new ObservatoryApi('inline')} server={configuration} settings={settings} onChange={change} disabled={false} />);
    const model = screen.getByRole('combobox', { name: 'Profilo modello' });
    for (const family of ['GPT-6', 'GPT-5.6', 'GPT-5.4', 'GPT-5']) {
      await user.click(screen.getByRole('tab', { name: family }));
      expect(screen.getByRole('tabpanel', { name: family })).toBeInTheDocument();
      expect(model).toHaveValue('gpt5');
    }
    expect(within(model).getAllByRole('option').map((option) => option.getAttribute('value'))).toEqual(configuration.models.map((item) => item.id));
    expect(within(model).queryByRole('option', { name: /GPT-5\.6|GPT-5\.4/ })).not.toBeInTheDocument();
    expect(change).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
