import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App, { parseRoute } from './App';
import { defaultPromptBlocks, runConfigurationSchema } from './contracts';
import type { ConversationRecord, Product, RunConfiguration, RunRecord, SubmitTurnRequest, Technology } from './contracts';
import { isRecord } from './lib/redaction';
import { demoMetadata } from './metadata';
import {
  comparisonScenarios, configurationFor, conversation, demoData, event, FakeEventSource, jsonResponse, liveConfigurationFor,
  modelCall, pathOf, product, promptPreview, run, scenario,
} from './test/fixtures';

function backend(technology: Technology = 'inline', catalog: readonly Product[] = [product]) {
  const base = `/api/${technology}`;
  let createdCount = 0;
  let currentConversation: ConversationRecord = { ...conversation, technology };
  let currentRun: RunRecord = run({ technology });
  const savedConversations = new Map<string, ConversationRecord>();
  const savedRuns = new Map<string, RunRecord>();
  let failNextSubmission = false;
  const submissions: SubmitTurnRequest[] = [];
  const previews: RunConfiguration[] = [];
  const fetchMock = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = pathOf(input);
    const method = init?.method ?? 'GET';
    if (method === 'POST' && path === `${base}/prompts/preview`) {
      const value = runConfigurationSchema.parse(typeof init?.body === 'string' ? JSON.parse(init.body) : null);
      previews.push(value);
      return jsonResponse(promptPreview(technology,
        `Anteprima della fixture per ${technology}; profilo ${value.promptProfile}.\n${JSON.stringify(value.promptBlocks)}`));
    }

    if (method === 'POST' && path === `${base}/conversations`) {
      currentConversation = { ...conversation, technology, id: `conversation-${++createdCount}`, messages: [] };
      savedConversations.set(currentConversation.id, currentConversation);
      return jsonResponse(currentConversation);
    }
    if (method === 'POST' && path === `${base}/conversations/${currentConversation.id}/turns`) {
      const value: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : null;
      if (!isRecord(value) || typeof value.message !== 'string' || typeof value.idempotencyKey !== 'string') {
        throw new Error('Invalid submitted test payload');
      }
      const request: SubmitTurnRequest = {
        message: value.message, idempotencyKey: value.idempotencyKey,
        configuration: runConfigurationSchema.parse(value.configuration),
      };
      submissions.push(request);
      if (failNextSubmission) {
        failNextSubmission = false;
        throw new Error('Connessione interrotta durante POST');
      }
      currentRun = run({
        id: `run-${savedRuns.size + 1}`, conversationId: currentConversation.id,
        technology, message: request.message, configuration: request.configuration,
      });
      savedRuns.set(currentRun.id, currentRun);
      return jsonResponse({
        runId: currentRun.id, conversationId: currentConversation.id,
        eventsUrl: `http://private-backend:8080/api/runs/${currentRun.id}/events`,
      }, 202);
    }
    if (method === 'POST' && path === `${base}/runs/run-1/cancel`) {
      currentRun = run({ ...currentRun, status: 'cancelled', completedAt: '2026-09-23T10:00:02Z' });
      savedRuns.set(currentRun.id, currentRun);
      return jsonResponse({ accepted: true });
    }
    if (method === 'POST' && path === `${base}/runs/run-1/replay`) {
      if (new Headers(init?.headers).get('Accept') !== 'text/event-stream') {
        return new Response(JSON.stringify({
          ...currentRun, replayOnly: true, originalRunId: currentRun.id, notice: 'Registrazione originale, nessuna riesecuzione.',
        }), {
          headers: { 'Content-Type': 'application/json', 'X-Original-Run-Id': currentRun.id, 'X-Replay-Only': 'true' },
        });
      }
      return new Response(currentRun.events.map((item) => `id: ${item.sequence}\ndata: ${JSON.stringify(item)}\n\n`).join(''), {
        headers: { 'Content-Type': 'text/event-stream', 'X-Original-Run-Id': currentRun.id, 'X-Replay-Only': 'true' },
      });
    }
    if (method === 'DELETE' && path === `${base}/runs`) {
      const deletedRuns = savedRuns.size;
      savedRuns.clear();
      for (const [id, record] of savedConversations) {
        const updated = {
          ...record,
          messages: record.messages.map((message) => ({ ...message, runId: null })),
        };
        savedConversations.set(id, updated);
        if (id === currentConversation.id) currentConversation = updated;
      }
      return jsonResponse({ deletedRuns });
    }
    if (method !== 'GET') throw new Error(`Unexpected test mutation ${method} ${path}`);
    if (path === `${base}/config`) return jsonResponse(liveConfigurationFor(technology));
    if (path === `${base}/products`) return jsonResponse(catalog);
    if (path === `${base}/demo-data`) return jsonResponse(demoData);
    if (path === `${base}/scenarios`) return jsonResponse([scenario]);
    if (path === `${base}/conversations`) return jsonResponse([...savedConversations.values()]);
    if (path === `${base}/runs`) return jsonResponse([...savedRuns.values()]);
    for (const record of savedRuns.values()) if (path === `${base}/runs/${record.id}`) return jsonResponse(record);
    for (const record of savedConversations.values()) if (path === `${base}/conversations/${record.id}`) return jsonResponse(record);
    throw new Error(`Unexpected test GET ${path}`);
  });
  vi.stubGlobal('fetch', fetchMock);
  return {
    fetchMock, submissions, previews, savedConversations, savedRuns,
    failNext: () => { failNextSubmission = true; },
    complete: () => {
      const answer = 'Il prodotto #83 costa 29,99 USD a listino. L’ordine sintetico è stato pagato 19,99 USD.';
      const selectedModel = currentRun.configuration.agentModels.router ?? currentRun.configuration.modelProfileId;
      const call = modelCall({
        id: `call-${savedRuns.size}`, runId: currentRun.id, modelProfileId: selectedModel,
        modelId: configurationFor(technology).models.find((model) => model.id === selectedModel)?.modelId ?? selectedModel,
      });
      currentRun = run({
        ...currentRun, status: 'completed', completedAt: '2026-09-23T10:00:02Z', durationMs: 2000,
        result: { answer, productIds: [83], sources: ['DummyJSON:83'] }, calls: [call],
        events: [
          event({ runId: currentRun.id }),
          event({ runId: currentRun.id, id: 'model-completed', sequence: 2, kind: 'model.completed', agent: 'router', data: call }),
          event({ runId: currentRun.id, id: 'run-completed', sequence: 3, kind: 'run.completed', message: 'Registrazione originale completata' }),
        ],
      });
      currentConversation = {
        ...currentConversation,
        messages: [
          ...currentConversation.messages,
          { id: `${currentRun.id}-user`, role: 'user', text: currentRun.message, at: '2026-09-23T10:00:00Z', runId: currentRun.id, productIds: [], sources: [] },
          { id: `${currentRun.id}-assistant`, role: 'assistant', text: answer, at: '2026-09-23T10:00:02Z', runId: currentRun.id, productIds: [], sources: ['DummyJSON:83'] },
        ],
      };
      savedRuns.set(currentRun.id, currentRun);
      savedConversations.set(currentConversation.id, currentConversation);
      return answer;
    },
  };
}

async function openChat(user: ReturnType<typeof userEvent.setup>, technology: Technology = 'inline') {
  const title = demoMetadata[technology].title;
  await user.click(screen.getByRole('button', { name: `Apri chat ${title}` }));
  return screen.findByRole('dialog', { name: `Chat con il Router · ${title}` });
}

async function authorizeLiveSend(user: ReturnType<typeof userEvent.setup>) {
  const previousRoute = window.location.hash;
  let budget = screen.queryByRole('spinbutton', { name: 'Budget approvato (USD)' });
  if (!budget) {
    await user.click(screen.getByRole('link', { name: 'Configurazione' }));
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    budget = await screen.findByRole('spinbutton', { name: 'Budget approvato (USD)' });
  }
  if ((budget as HTMLInputElement).value !== '0.1') {
    await user.clear(budget);
    await user.type(budget, '0.1');
  }
  await user.click(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ }));
  if (window.location.hash !== previousRoute) {
    await act(async () => { window.location.hash = previousRoute; });
  }
}

describe('routing isolato per demo', () => {
  it('supporta home, tre demo e pagine valide senza trasformare route arbitrarie in API', () => {
    expect(parseRoute('#/')).toEqual({ kind: 'home' });
    expect(parseRoute('#/confronto')).toEqual({ kind: 'comparison' });
    expect(parseRoute('#/dati')).toEqual({ kind: 'data' });
    expect(parseRoute('#/inline')).toEqual({ kind: 'demo', technology: 'inline', page: 'chat' });
    expect(parseRoute('#/skills/trace')).toEqual({ kind: 'demo', technology: 'skills', page: 'trace' });
    expect(parseRoute('#/inline/agents')).toEqual({ kind: 'demo', technology: 'inline', page: 'trace' });
    expect(parseRoute('#/inline/data')).toEqual({ kind: 'data', technology: 'inline' });
    expect(parseRoute('#/a2a/history')).toEqual({ kind: 'demo', technology: 'a2a', page: 'history' });
    expect(parseRoute('#/unknown/data')).toEqual({ kind: 'not-found' });
    expect(parseRoute('#/other/products')).toEqual({ kind: 'not-found' });
    expect(parseRoute('#/inline/not-a-page')).toEqual({ kind: 'not-found' });
  });

  it('mostra Traccia al posto della pagina ridondante Agenti e servizi', async () => {
    window.history.replaceState(null, '', '/#/inline');
    render(<App />);
    await screen.findByRole('link', { name: 'Traccia' });
    expect(screen.queryByRole('link', { name: 'Agenti e servizi' })).not.toBeInTheDocument();
    window.location.hash = '#/inline/agents';
    await screen.findByRole('heading', { name: 'Dal messaggio all’ultimo evento' });
  });
});

describe('flusso applicazione su API reali simulate nel test', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/#/inline');
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
  });

  it('apertura e pulsanti guidati non fanno POST; consenso esplicito, SSE, prodotto correlato e GET finale', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    expect(screen.queryByRole('region', { name: 'Catalogo pubblico DummyJSON' })).not.toBeInTheDocument();
    expect(api.fetchMock.mock.calls.some(([input]) => pathOf(input).endsWith('/demo-data'))).toBe(false);
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(0);
    await openChat(user);
    expect(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ })).not.toBeChecked();
    await user.click(screen.getByText('Percorso guidato · sei turni'));
    const consent = screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ });
    expect(consent).not.toBeChecked();
    await user.click(screen.getByRole('button', { name: /Confermo solo una bozza sintetica/ }));
    expect(consent).not.toBeChecked();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('Confermo solo una bozza sintetica.');
    expect(api.submissions).toHaveLength(0);
    await user.click(consent);
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(api.submissions).toHaveLength(1);
    expect(api.submissions[0]?.configuration.confirmAction).toBe(true);
    expect(JSON.stringify(api.submissions[0])).not.toMatch(/thumbnail|images|dummyjson|image_url/i);
    expect(consent).not.toBeChecked();
    const stream = FakeEventSource.instances[0];
    expect(stream?.url).toBe('/api/inline/runs/run-1/events');
    await act(async () => {
      const delta = event({ kind: 'answer.delta', message: 'Risposta parziale unica' });
      stream?.emit(delta);
      stream?.emit(delta);
    });
    expect(screen.getByText('Risposta parziale unica')).toBeInTheDocument();
    expect(screen.queryByText('Risposta parziale unicaRisposta parziale unica')).not.toBeInTheDocument();
    const answer = api.complete();
    await act(async () => { stream?.emit(event({ id: 'event-end', sequence: 2, kind: 'run.completed' })); });
    await screen.findByText(answer);
    await waitFor(() => expect(screen.getAllByRole('img', { name: product.title })).toHaveLength(1));
    expect(stream?.closed).toBe(true);
    expect(api.submissions).toHaveLength(1);
    expect(api.fetchMock.mock.calls.some(([input]) => pathOf(input) === '/api/inline/conversations/conversation-1')).toBe(true);
    expect(screen.getByLabelText('Consumi del run selezionato')).toHaveTextContent('Input: Non disponibile');
    expect(screen.getByLabelText('Consumi del run selezionato')).toHaveTextContent('Costo stimato: Non disponibile');
    await user.click(screen.getByRole('link', { name: 'Dettaglio per modello' }));
    await screen.findByRole('heading', { name: 'Token e costi' });
    expect(screen.getByRole('table', { name: 'Riepilogo token e prezzo per agente' })).toBeInTheDocument();
    expect(screen.getAllByText('Non disponibile').length).toBeGreaterThan(0);
    expect(api.submissions).toHaveLength(1);
    await user.click(screen.getByRole('link', { name: 'Storico ed esperimenti' }));
    await screen.findByRole('button', { name: 'Riproduci la registrazione del run run-1' });
    await user.click(screen.getByRole('button', { name: 'Riproduci la registrazione del run run-1' }));
    await screen.findByText('Replay SSE completato · 3 eventi ricevuti.');
    expect(screen.getByText('REPLAY · LIVE')).toBeInTheDocument();
    expect(screen.getByText(/Nessun nuovo run e nessuna chiamata modello/)).toBeInTheDocument();
    expect(api.submissions).toHaveLength(1);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(api.fetchMock.mock.calls.filter(([input, init]) => init?.method === 'POST' && pathOf(input) === '/api/inline/runs/run-1/replay')).toHaveLength(1);
  });

  it('un invio incerto si ritenta solo al click con stessa chiave e stesso payload', async () => {
    const user = userEvent.setup();
    const api = backend();
    api.failNext();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByRole('button', { name: 'Dettagliato coerente' }));
    await openChat(user);
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica il prodotto #83.');
    const consent = screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ });
    await user.click(consent);
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await screen.findByText('Invio non confermato');
    expect(api.submissions).toHaveLength(1);
    expect(api.submissions[0]?.configuration.promptBlocks).toEqual({
      ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true,
    });
    expect(screen.getByRole('button', { name: 'Invia' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Genera anteprima dal server' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Riprova lo stesso invio' }));
    await waitFor(() => expect(api.submissions).toHaveLength(2));
    expect(api.submissions[0]).toEqual(api.submissions[1]);
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(consent).not.toBeChecked();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('');
    await act(async () => { FakeEventSource.instances[0]?.fail(); });
    await screen.findByText(/Stream interrotto/);
    await user.click(screen.getByRole('button', { name: 'Riconnetti SSE' }));
    expect(api.submissions).toHaveLength(2);
    expect(FakeEventSource.instances).toHaveLength(2);
  });

  it('la home non effettua alcuna chiamata API e offre tre demo separate', () => {
    window.history.replaceState(null, '', '/#/');
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);
    expect(screen.getByRole('heading', { name: 'Agent-to-Agent' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Agent Skills' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Inline' })).toBeInTheDocument();
    expect(screen.getByText('Un router. Tre servizi.')).toBeInTheDocument();
    expect(screen.queryByText('Un caso. Quattro agenti.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Apri chat/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each(['inline', 'skills'] as const)('passando da A2A a %s non conserva override specialisti o consenso', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', '/#/a2a');
    const api = backend(technology);
    const fetchTarget = api.fetchMock.getMockImplementation();
    api.fetchMock.mockImplementation(async (input, init) => {
      const path = pathOf(input);
      if (path.startsWith('/api/a2a/')) {
        if (init?.method !== 'GET') throw new Error('Il cambio tecnologia non deve eseguire richieste A2A.');
        if (path.endsWith('/config')) return jsonResponse(configurationFor('a2a'));
        if (path.endsWith('/products')) return jsonResponse([product]);
        if (path.endsWith('/scenarios')) return jsonResponse([scenario]);
        return jsonResponse([]);
      }
      if (!fetchTarget) throw new Error('Backend fixture mancante.');
      return fetchTarget(input, init);
    });
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    for (const name of ['Router', 'Catalog', 'Orders', 'Returns']) {
      await user.selectOptions(screen.getByRole('combobox', { name }), 'gpt6-luna');
    }
    await user.click(screen.getByRole('button', { name: 'Dettagliato coerente' }));
    await openChat(user, 'a2a');
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), 'Solo nella demo A2A.');
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('link', { name: technology === 'inline' ? 'Inline' : 'Agent Skills' }));
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await openChat(user, technology);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    expect(screen.getByRole('combobox', { name: 'Router' })).toHaveValue('');
    for (const name of ['Catalog', 'Orders', 'Returns']) expect(screen.queryByRole('combobox', { name })).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).not.toBeChecked();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('');
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).not.toBeChecked();
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica ORD-1042.');
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(api.submissions).toHaveLength(1));
    expect(api.submissions[0]?.configuration).toMatchObject({ agentModels: {}, confirmAction: false, mode: 'live', promptBlocks: defaultPromptBlocks });
    expect(JSON.stringify(api.submissions[0])).not.toMatch(/thumbnail|images|image_url/i);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('%s invia i blocchi scelti alla preview e al turno, senza eseguire al cambio delle opzioni', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', `/#/${technology}`);
    const api = backend(technology);
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    const blocks = { ...defaultPromptBlocks, checklist: true, outputContract: true, examples: true };
    await user.click(screen.getByRole('button', { name: 'Dettagliato coerente' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Profilo prompt' }), 'gpt5');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Profilo modello' }), 'gpt6-sol');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Profilo modello' }), 'gpt5');
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).toBeChecked();
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findAllByText(new RegExp(`Anteprima della fixture per ${technology}; profilo gpt5`));
    expect(api.previews).toHaveLength(1);
    expect(api.previews[0]).toMatchObject({ promptBlocks: blocks, promptProfile: 'gpt5', confirmAction: false });
    expect(api.submissions).toHaveLength(0);
    expect(api.fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
    expect(FakeEventSource.instances).toHaveLength(0);

    await openChat(user, technology);
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica il prodotto #83.');
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(api.submissions).toHaveLength(1);
    expect(api.submissions[0]?.configuration).toMatchObject({ promptBlocks: blocks, promptProfile: 'gpt5', confirmAction: false });
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Genera anteprima dal server' })).toBeDisabled();
    const answer = api.complete();
    await act(async () => { FakeEventSource.instances[0]?.emit(event({ id: 'run-completed', sequence: 3, kind: 'run.completed' })); });
    await screen.findByText(answer);
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).toBeEnabled();
    expect(screen.getByRole('checkbox', { name: 'Checklist di verifica' })).toBeChecked();
    expect(screen.getByRole('combobox', { name: 'Profilo prompt' })).toHaveValue('gpt5');
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('button', { name: 'Nuova chat' }));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).not.toBeChecked());
    for (const name of ['Checklist di verifica', 'Contratto della risposta', 'Esempi di risposta']) {
      expect(screen.getByRole('checkbox', { name })).toBeChecked();
    }
    expect(screen.getByRole('combobox', { name: 'Profilo prompt' })).toHaveValue('gpt5');
    expect(api.submissions).toHaveLength(1);
    expect(api.previews).toHaveLength(1);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('%s applica GPT-5/BAD → GPT-6/GOOD solo al prossimo invio e conserva conversazione e snapshot', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', `/#/${technology}`);
    const api = backend(technology);
    render(<App />);
    const model = await screen.findByRole('combobox', { name: 'Profilo modello' });
    const prompt = screen.getByRole('combobox', { name: 'Profilo prompt' });
    await user.selectOptions(prompt, 'bad');
    await user.click(screen.getByRole('tab', { name: 'GPT-6' }));
    expect(model).toHaveValue('gpt5');
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(0);

    await openChat(user, technology);
    const nextSettings = screen.getByRole('region', { name: 'Impostazioni del prossimo messaggio' });
    expect(nextSettings).toHaveTextContent('GPT-5 (gpt5)');
    expect(nextSettings).toHaveTextContent('BAD');
    expect(screen.getByText(/LIVE: il prossimo Invio può effettuare chiamate reali a pagamento/)).toBeInTheDocument();
    const firstText = 'Cerca vestiti rossi.';
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), firstText);
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(api.submissions[0]?.configuration).toMatchObject({ modelProfileId: 'gpt5', promptProfile: 'bad', mode: 'live', approvedBudgetUsd: 0.1 });
    expect(model).toBeDisabled();
    expect(prompt).toBeDisabled();
    expect(await screen.findByRole('region', { name: 'Impostazioni registrate nel run' })).toHaveTextContent('GPT-5 (gpt5)');
    const originalConfiguration = structuredClone(api.savedRuns.get('run-1')?.configuration);

    api.complete();
    await act(async () => { FakeEventSource.instances[0]?.emit(event({ id: 'completed-1', sequence: 3, kind: 'run.completed' })); });
    await waitFor(() => expect(model).toBeEnabled());
    await user.selectOptions(model, 'gpt6-astra');
    expect(prompt).toHaveValue('bad');
    await user.selectOptions(prompt, 'good');
    expect(model).toHaveValue('gpt6-astra');
    await user.click(screen.getByRole('tab', { name: 'GPT-5' }));
    expect(model).toHaveValue('gpt6-astra');
    expect(nextSettings).toHaveTextContent('GPT-6 Astra (gpt6-astra)');
    expect(nextSettings).toHaveTextContent('GOOD');
    const recordedSettings = screen.getByRole('region', { name: 'Impostazioni registrate nel run' });
    expect(recordedSettings).toHaveTextContent('GPT-5 (gpt5)');
    expect(recordedSettings).toHaveTextContent('BAD');
    expect(recordedSettings).not.toHaveTextContent('gpt6-astra');
    expect(api.savedRuns.get('run-1')?.configuration).toEqual(originalConfiguration);
    expect(screen.getByRole('combobox', { name: 'Conversazione' })).toHaveValue('conversation-1');
    expect(screen.getByRole('list', { name: 'Messaggi registrati' })).toHaveTextContent(firstText);
    expect(api.submissions).toHaveLength(1);
    expect(api.previews).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(api.fetchMock.mock.calls.filter(([input, init]) => init?.method === 'POST' && pathOf(input).endsWith('/conversations'))).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findAllByText(new RegExp(`Anteprima della fixture per ${technology}; profilo good`));
    expect(api.previews).toHaveLength(1);
    expect(api.previews[0]).toMatchObject({ modelProfileId: 'gpt6-astra', promptProfile: 'good', mode: 'live' });
    expect(api.submissions).toHaveLength(1);
    const secondText = 'Mi dici quanti vestiti rossi hai?';
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), secondText);
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(2));
    expect(api.submissions).toHaveLength(2);
    expect(api.submissions[1]?.message).toBe(secondText);
    expect(api.submissions[1]?.configuration).toEqual(api.previews[0]);
    expect(api.savedRuns.get('run-2')).toMatchObject({
      conversationId: 'conversation-1', configuration: { modelProfileId: 'gpt6-astra', promptProfile: 'good' },
    });
    expect(api.savedRuns.get('run-1')?.configuration).toEqual(originalConfiguration);
    expect(api.fetchMock.mock.calls.filter(([input, init]) => init?.method === 'POST' && pathOf(input).endsWith('/conversations'))).toHaveLength(1);

    api.complete();
    await act(async () => { FakeEventSource.instances[1]?.emit(event({ runId: 'run-2', id: 'completed-2', sequence: 3, kind: 'run.completed' })); });
    await waitFor(() => expect(screen.getByRole('list', { name: 'Messaggi registrati' }).children).toHaveLength(4));
    expect(screen.getByRole('list', { name: 'Messaggi registrati' })).toHaveTextContent(firstText);
    expect(screen.getByRole('list', { name: 'Messaggi registrati' })).toHaveTextContent(secondText);
    await user.click(screen.getByRole('button', { name: 'Nuova chat' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Conversazione' })).toHaveValue('conversation-2'));
    expect(model).toHaveValue('gpt6-astra');
    expect(prompt).toHaveValue('good');
    expect(api.savedRuns.get('run-1')?.configuration).toEqual(originalConfiguration);
    expect(api.submissions).toHaveLength(2);
    expect(FakeEventSource.instances).toHaveLength(2);
  });

  it('un invio in attesa non cancella una nuova domanda preparata dal catalogo', async () => {
    const user = userEvent.setup();
    const api = backend();
    const originalFetch = api.fetchMock.getMockImplementation();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => { release = resolve; });
    api.fetchMock.mockImplementation(async (input, init) => {
      if (init?.method === 'POST' && pathOf(input) === '/api/inline/conversations') await gate;
      if (!originalFetch) throw new Error('Backend fixture mancante.');
      return originalFetch(input, init);
    });
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByRole('link', { name: 'Dati della demo' }));
    await screen.findByRole('heading', { name: 'Dati usati dagli agenti' });
    await openChat(user);
    const composer = screen.getByRole('textbox', { name: 'Messaggio al Router' });
    await user.type(composer, 'Prima richiesta.');
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    expect(composer).toBeDisabled();
    await user.click(screen.getByRole('button', { name: `Prepara una domanda su ${product.title}, prodotto ${product.id}` }));
    const newerDraft = `Vorrei informazioni sul prodotto #${product.id}: ${product.title}.`;
    expect(composer).toHaveValue(newerDraft);
    await act(async () => { release(); });
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(api.submissions).toHaveLength(1);
    expect(api.submissions[0]?.message).toBe('Prima richiesta.');
    expect(composer).toHaveValue(newerDraft);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('%s prepara e invia la configurazione esatta del preset, senza invii impliciti', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', `/#/${technology}`);
    const api = backend(technology), originalFetch = api.fetchMock.getMockImplementation();
    api.fetchMock.mockImplementation(async (input, init) => {
      if (pathOf(input) === `/api/${technology}/scenarios`) return jsonResponse(comparisonScenarios);
      if (!originalFetch) throw new Error('Backend fixture mancante');
      return originalFetch(input, init);
    });
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Router' }), 'gpt6-luna');
    await openChat(user, technology);
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('link', { name: 'Confronti guidati' }));
    await user.click(await screen.findByRole('button', { name: 'Prepara C · GPT-5 / GOOD + ridondanza · LIVE' }));
    expect(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' })).toHaveValue('correction');
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('Voglio restituire ORD-1042 per ripensamento.');
    expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).not.toBeChecked();
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(0);
    await authorizeLiveSend(user);
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(api.submissions).toHaveLength(1));
    expect(api.submissions[0]?.configuration).toEqual({
      mode: 'live', modelProfileId: 'gpt5', agentModels: {}, promptProfile: 'good',
      promptBlocks: { checklist: false, outputContract: false, examples: false, redundancy: true, conflictingStyle: false },
      historyStrategy: 'full', toolTransport: 'direct', confirmAction: false, maxOutputTokens: 1500, maxModelCalls: 24,
      approvedBudgetUsd: 0.1,
    });
    expect(JSON.stringify(api.submissions[0])).not.toMatch(/expectedFacts|scenarioId|thumbnail|images/);
    for (const button of screen.getAllByRole('button', { name: /^Prepara .* · LIVE$/ })) expect(button).toBeDisabled();
    api.complete();
    await act(async () => { FakeEventSource.instances[0]?.emit(event({ kind: 'run.completed' })); });
    await screen.findByText(/C’è già una prova nello storico corrente/);
    for (const button of screen.getAllByRole('button', { name: /^Prepara .* · LIVE$/ })) expect(button).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Nuova chat' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Prepara B · GPT-6 Sol / GOOD · LIVE' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Prepara B · GPT-6 Sol / GOOD · LIVE' }));
    expect(screen.getByRole('combobox', { name: 'Scenario DEVELOPMENT' })).toHaveValue('catalog-shirts');
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('Consigliami una camicia sotto i 40 dollari.');
    expect(api.submissions).toHaveLength(1);
  });

  it('rispetta il default LIVE del backend e preparare un preset revoca entrambi i consensi senza inviare', async () => {
    const user = userEvent.setup(), api = backend(), originalFetch = api.fetchMock.getMockImplementation();
    api.fetchMock.mockImplementation(async (input, init) => {
      const path = pathOf(input);
      if (path === '/api/inline/config') return jsonResponse(liveConfigurationFor('inline'));
      if (path === '/api/inline/scenarios') return jsonResponse(comparisonScenarios);
      if (!originalFetch) throw new Error('Backend fixture mancante');
      return originalFetch(input, init);
    });
    render(<App />);
    expect(await screen.findByText('LIVE')).toBeInTheDocument();
    await openChat(user);
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ }));
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('link', { name: 'Confronti guidati' }));
    await user.click(await screen.findByRole('button', { name: 'Prepara B · GPT-6 Sol / GOOD · LIVE' }));
    expect(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Invia' })).toBeDisabled();
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('Consigliami una camicia sotto i 40 dollari.');
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(api.submissions).toHaveLength(0);
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo questo invio LIVE/ }));
    await user.click(screen.getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(api.submissions).toHaveLength(1));
    expect(api.submissions[0]?.configuration).toMatchObject({ mode: 'live', modelProfileId: 'gpt6-sol', approvedBudgetUsd: 0.1 });
  });

  it('l’Inspector conserva la preview come HTTP browser con i blocchi, non come chiamata modello', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByRole('checkbox', { name: 'Stile ridondante' }));
    await user.click(screen.getByRole('button', { name: 'Genera anteprima dal server' }));
    await screen.findByText(/Anteprima della fixture per inline/);
    await user.click(screen.getByRole('link', { name: 'Inspector delle richieste' }));
    await screen.findByRole('heading', { name: 'Che cosa è stato realmente registrato' });
    expect(screen.getByRole('heading', { name: 'Nessuna chiamata modello registrata' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Richiesta HTTP' })).toHaveDisplayValue(/POST \/api\/inline\/prompts\/preview/);
    const captured = screen.getByLabelText('Corpo inviato dal browser (null = nessun corpo)');
    expect(captured).toHaveTextContent('"promptBlocks"');
    expect(captured).toHaveTextContent('"redundancy": true');
    expect(screen.getByText(/non sono chiamate wire al modello/)).toBeInTheDocument();
    expect(api.submissions).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('non trasferisce il consenso alla bozza a una nuova conversazione', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await openChat(user);
    const consent = screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ });
    await user.click(consent);
    expect(consent).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Nuova chat' }));
    await waitFor(() => expect(consent).not.toBeChecked());
    expect(api.submissions).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('%s mantiene una sola chat e la bozza attraverso Traccia, Inspector e Costi', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', `/#/${technology}`);
    const api = backend(technology);
    const { container } = render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    expect(screen.queryByRole('textbox', { name: 'Messaggio al Router' })).not.toBeInTheDocument();
    const title = demoMetadata[technology].title;
    await openChat(user, technology);
    const composer = screen.getByRole('textbox', { name: 'Messaggio al Router' });
    await user.type(composer, 'Bozza da confrontare, non ancora inviata.');
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('button', { name: `Chiudi chat ${title}` }));
    expect(screen.getByRole('button', { name: `Apri chat ${title}` })).toHaveFocus();
    expect(composer).toHaveValue('Bozza da confrontare, non ancora inviata.');
    await openChat(user, technology);
    expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).toBeChecked();
    for (const [label, heading] of [
      ['Dati della demo', 'Dati usati dagli agenti'],
      ['Traccia', 'Dal messaggio all’ultimo evento'],
      ['Inspector', 'Che cosa è stato realmente registrato'],
      ['Token e costi', 'Token e costi'],
    ] as const) {
      await user.click(screen.getByRole('link', { name: label }));
      await screen.findByRole('heading', { name: heading });
      if (label === 'Dati della demo') {
        const tabs = screen.getByRole('navigation', { name: 'Pagine del laboratorio' });
        expect(within(tabs).queryByRole('link', { name: 'Dati della demo' })).not.toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Dati della demo' })).toHaveAttribute('href', '#/dati');
      }
      expect(screen.getByRole('dialog', { name: `Chat con il Router · ${title}` })).toBeInTheDocument();
      expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toBe(composer);
      expect(composer).toHaveValue('Bozza da confrontare, non ancora inviata.');
      expect(container.querySelectorAll('#chat-message')).toHaveLength(1);
    }
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(api.submissions).toHaveLength(0);
    expect(api.previews).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('Chiedi del prodotto apre il bot ma il catalogo completo resta soltanto nella pagina', async () => {
    const user = userEvent.setup();
    const catalog = Array.from({ length: 38 }, (_, index) => ({ ...product, id: 83 + index, title: `Prodotto ${83 + index}` }));
    const api = backend('inline', catalog);
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await user.click(screen.getByRole('link', { name: 'Dati della demo' }));
    await screen.findByText('38 di 38 prodotti');
    expect(screen.getAllByRole('article')).toHaveLength(38);
    await user.click(screen.getByRole('button', { name: 'Prepara una domanda su Prodotto 83, prodotto 83' }));
    const panel = screen.getByRole('dialog', { name: 'Chat con il Router · Inline' });
    const composer = within(panel).getByRole('textbox', { name: 'Messaggio al Router' });
    expect(composer).toHaveFocus();
    expect(composer).toHaveValue('Vorrei informazioni sul prodotto #83: Prodotto 83.');
    expect(within(panel).queryByRole('searchbox')).not.toBeInTheDocument();
    expect(within(panel).queryByRole('article')).not.toBeInTheDocument();
    const consent = within(panel).getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ });
    await user.click(consent);
    await user.click(screen.getByRole('button', { name: 'Prepara una domanda su Prodotto 84, prodotto 84' }));
    expect(composer).toHaveValue('Vorrei informazioni sul prodotto #84: Prodotto 84.');
    expect(consent).not.toBeChecked();
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(api.submissions).toHaveLength(0);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('%s separa dati e configurazione conservando modello e chat, senza inviare ordini automaticamente', async (technology) => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', `/#/${technology}`);
    const api = backend(technology);
    render(<App />);
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Profilo modello' }), 'gpt6-sol');
    expect(api.fetchMock.mock.calls.some(([input]) => pathOf(input).endsWith('/demo-data'))).toBe(false);
    await openChat(user, technology);
    const composer = screen.getByRole('textbox', { name: 'Messaggio al Router' });
    await user.type(composer, 'Bozza conservata');
    await user.click(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ }));
    await user.click(screen.getByRole('link', { name: 'Dati della demo' }));
    expect(composer).toHaveValue('Bozza conservata');
    expect(screen.queryByRole('combobox', { name: 'Profilo modello' })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Ordini (2)' }));
    await user.click(screen.getByRole('button', { name: "Prepara una domanda sull'ordine ORD-1042" }));
    expect(composer).toHaveValue("Mostra l'ordine ORD-1042.");
    expect(screen.getByRole('checkbox', { name: /Autorizzo solo bozza sintetica/ })).not.toBeChecked();
    await user.click(screen.getByRole('link', { name: 'Configurazione' }));
    expect(await screen.findByRole('combobox', { name: 'Profilo modello' })).toHaveValue('gpt6-sol');
    expect(screen.queryByRole('region', { name: 'Catalogo pubblico DummyJSON' })).not.toBeInTheDocument();
    expect(composer).toHaveValue("Mostra l'ordine ORD-1042.");
    expect(api.fetchMock.mock.calls.filter(([input]) => pathOf(input) === `/api/${technology}/demo-data`)).toHaveLength(1);
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(api.submissions).toHaveLength(0);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('chiudere il bot non cancella il run o SSE; le risposte arrivano alla stessa conversazione', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    await openChat(user);
    await user.type(screen.getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica il prodotto #83.');
    await authorizeLiveSend(user);
    await user.keyboard('{Control>}{Enter}{/Control}');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    const stream = FakeEventSource.instances[0];
    await user.click(screen.getByRole('button', { name: 'Chiudi chat Inline' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apri chat Inline' })).toHaveAccessibleDescription('Turno in corso');
    expect(screen.getByRole('button', { name: 'Annulla run' })).toBeEnabled();
    expect(stream?.closed).toBe(false);
    expect(api.fetchMock.mock.calls.some(([input, init]) => init?.method === 'POST' && pathOf(input).endsWith('/cancel'))).toBe(false);
    await user.click(screen.getByRole('link', { name: 'Inspector' }));
    await screen.findByRole('heading', { name: 'Che cosa è stato realmente registrato' });
    const answer = api.complete();
    await act(async () => { stream?.emit(event({ id: 'run-completed', sequence: 3, kind: 'run.completed' })); });
    await waitFor(() => expect(stream?.closed).toBe(true));
    const panel = await openChat(user);
    await within(panel).findByText(answer);
    expect(within(panel).getByRole('combobox', { name: 'Conversazione' })).toHaveValue('conversation-1');
    expect(within(panel).getAllByRole('img', { name: product.title })).toHaveLength(1);
    expect(api.submissions).toHaveLength(1);
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('il controllo Annulla run resta operativo nel pannello ed è distinto dalla chiusura', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    const panel = await openChat(user);
    await user.type(within(panel).getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica ORD-1042.');
    await authorizeLiveSend(user);
    await user.click(within(panel).getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    await user.click(within(panel).getByRole('button', { name: 'Annulla run' }));
    await waitFor(() => expect(api.fetchMock.mock.calls.filter(([input, init]) =>
      init?.method === 'POST' && pathOf(input) === '/api/inline/runs/run-1/cancel')).toHaveLength(1));
    expect(panel).toBeVisible();
    await within(panel).findByText('Cancellazione richiesta al backend. Lo stato definitivo è letto dal run.');
    expect(api.submissions).toHaveLength(1);
  });

  it('cancella lo storico solo dopo conferma e conserva i messaggi delle conversazioni', async () => {
    const user = userEvent.setup();
    const api = backend();
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    const panel = await openChat(user);
    await user.type(within(panel).getByRole('textbox', { name: 'Messaggio al Router' }), 'Verifica il prodotto #83.');
    await authorizeLiveSend(user);
    await user.click(within(panel).getByRole('button', { name: 'Invia' }));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    const answer = api.complete();
    await act(async () => { FakeEventSource.instances[0]?.emit(event({ id: 'run-completed', sequence: 3, kind: 'run.completed' })); });
    await within(panel).findByText(answer);
    await user.click(within(panel).getByRole('button', { name: 'Chiudi chat Inline' }));
    await user.click(screen.getByRole('link', { name: 'Storico ed esperimenti' }));
    await screen.findByRole('button', { name: 'Riproduci la registrazione del run run-1' });

    await user.click(screen.getByRole('button', { name: 'Cancella tutte' }));
    expect(screen.getByRole('alert')).toHaveTextContent('I testi delle conversazioni restano.');
    await user.click(screen.getByRole('button', { name: 'Annulla' }));
    expect(api.savedRuns.size).toBe(1);
    expect(api.fetchMock.mock.calls.some(([input, init]) => init?.method === 'DELETE' && pathOf(input) === '/api/inline/runs')).toBe(false);

    await user.click(screen.getByRole('button', { name: 'Cancella tutte' }));
    await user.click(screen.getByRole('button', { name: 'Conferma cancellazione' }));
    await waitFor(() => expect(api.fetchMock.mock.calls.some(
      ([input, init]) => init?.method === 'DELETE' && pathOf(input) === '/api/inline/runs',
    )).toBe(true));
    await screen.findByText('Esecuzioni cancellate: 1. I testi delle conversazioni restano disponibili.');
    await screen.findByText('Lo storico è ancora vuoto');
    expect(api.savedRuns.size).toBe(0);
    const savedConversation = api.savedConversations.get('conversation-1');
    expect(savedConversation?.messages.map((message) => message.text)).toEqual([
      'Verifica il prodotto #83.', answer,
    ]);
    expect(savedConversation?.messages.every((message) => message.runId === null)).toBe(true);
    const preservedPanel = await openChat(user);
    await within(preservedPanel).findByText(answer);
    expect(within(preservedPanel).getByText('Verifica il prodotto #83.')).toBeInTheDocument();
  });

  it('apre dalla pagina di Inspector e il link impostazioni ritorna alla demo corretta senza invii', async () => {
    const user = userEvent.setup();
    window.history.replaceState(null, '', '/#/skills/inspector');
    const api = backend('skills');
    render(<App />);
    await screen.findByRole('heading', { name: 'Che cosa è stato realmente registrato' });
    const panel = await openChat(user, 'skills');
    await user.type(within(panel).getByRole('textbox', { name: 'Messaggio al Router' }), 'Bozza skills');
    await user.click(within(panel).getByRole('link', { name: 'Apri configurazione della demo Agent Skills' }));
    await screen.findByRole('combobox', { name: 'Profilo prompt' });
    expect(window.location.hash).toBe('#/skills/chat');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await openChat(user, 'skills');
    expect(screen.getByRole('textbox', { name: 'Messaggio al Router' })).toHaveValue('Bozza skills');
    expect(api.fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(api.submissions).toHaveLength(0);
  });

  it('silenzia log e alert del pannello nascosto senza perdere gli errori da mostrare alla riapertura', async () => {
    const user = userEvent.setup();
    const api = backend();
    const normal = api.fetchMock.getMockImplementation();
    api.fetchMock.mockImplementation(async (input, init) => {
      const path = pathOf(input);
      if (path === '/api/inline/scenarios') return jsonResponse({ message: 'Scenari non disponibili dalla fixture' }, 503);
      if (path === '/api/inline/conversations' && init?.method === 'GET') return jsonResponse({ message: 'Elenco conversazioni non disponibile dalla fixture' }, 503);
      if (!normal) throw new Error('Backend fixture mancante.');
      return normal(input, init);
    });
    render(<App />);
    await screen.findByRole('combobox', { name: 'Profilo modello' });
    const log = screen.getByRole('log', { hidden: true });
    expect(log).toHaveAttribute('aria-live', 'off');
    const panel = await openChat(user);
    await within(panel).findByText('Elenco conversazioni non disponibile dalla fixture');
    await user.click(within(panel).getByText('Percorso guidato · sei turni'));
    await within(panel).findByText('Scenari non disponibili dalla fixture');
    expect(log).toHaveAttribute('aria-live', 'polite');
    await user.click(within(panel).getByRole('button', { name: 'Chiudi chat Inline' }));
    expect(panel).toHaveAttribute('inert');
    expect(panel).toHaveAttribute('aria-hidden', 'true');
    expect(log).toHaveAttribute('aria-live', 'off');
    expect(log).not.toBeVisible();
    expect(screen.queryAllByRole('alert', { hidden: true })).toHaveLength(0);
    const requests = api.fetchMock.mock.calls.length;
    await openChat(user);
    expect(within(panel).getByText('Elenco conversazioni non disponibile dalla fixture')).toBeVisible();
    expect(within(panel).getByText('Scenari non disponibili dalla fixture')).toBeVisible();
    expect(api.fetchMock).toHaveBeenCalledTimes(requests);
    expect(api.submissions).toHaveLength(0);
  });
});
