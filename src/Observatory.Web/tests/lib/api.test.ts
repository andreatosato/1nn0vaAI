// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';
import { buildTurnRequest, ObservatoryApi, validateConfiguration, validatePreviewConfiguration } from './api';
import type { HttpCapture } from './api';
import { configuration, configurationFor, event, jsonResponse, liveConfigurationFor, product, promptPreview, settings } from '../test/fixtures';

describe('client API tipizzato', () => {
  it('senza limiti richiede opt-in backend e conserva il consenso esplicito nella richiesta', () => {
    const server = liveConfigurationFor('a2a');
    const selected = { ...settings, mode: 'live', unboundedExecution: true, approvedBudgetUsd: null };
    expect(() => validateConfiguration(selected, server)).toThrow('senza limiti non abilitata');
    const enabled = { ...server, capabilities: { ...server.capabilities, allowUnboundedExecution: true } };
    expect(buildTurnRequest('Ordine ORD-1042', selected, enabled, 'unbounded-test').configuration).toEqual(selected);
    expect(() => validateConfiguration({ ...selected, approvedBudgetUsd: 0.1 }, enabled)).toThrow('Rimuovi il budget');
    expect(() => validateConfiguration({ ...selected, unboundedExecution: false }, enabled)).toThrow('budget approvato');
    expect(() => validateConfiguration(selected, { ...enabled, allowLive: false })).toThrow('LIVE non');
  });
  it('costruisce il turno solo da testo, idempotency key e configurazione', () => {
    const server = liveConfigurationFor('inline');
    const body = buildTurnRequest('  Parlami del prodotto #83  ', { ...settings, approvedBudgetUsd: 0.1 }, server, 'stable-id');
    expect(Object.keys(body)).toEqual(['message', 'idempotencyKey', 'configuration']);
    expect(body.message).toBe('Parlami del prodotto #83');
    expect(body.idempotencyKey).toBe('stable-id');
    expect(body.configuration.confirmAction).toBe(false);
    expect(JSON.stringify(body)).not.toMatch(/thumbnail|images|dummyjson|image_url/i);
    expect(() => buildTurnRequest(' ', settings, configuration, 'key')).toThrow('Scrivi un messaggio');
  });

  it('il payload validato conserva uno snapshot indipendente dalle successive modifiche UI, anche annidate', () => {
    const server = liveConfigurationFor('inline');
    const selected = {
      ...settings, approvedBudgetUsd: 0.1, agentModels: { router: 'gpt5' }, promptBlocks: { ...settings.promptBlocks },
    };
    const original = structuredClone(selected);
    const submitted = buildTurnRequest('Quanti vestiti rossi hai?', selected, server, 'first-turn');
    selected.modelProfileId = 'gpt6-astra';
    selected.promptProfile = 'bad';
    selected.agentModels.router = 'gpt6-luna';
    selected.promptBlocks.checklist = true;
    expect(submitted.configuration).toEqual(original);
    expect(submitted.configuration).not.toBe(selected);
    expect(submitted.configuration.agentModels).not.toBe(selected.agentModels);
    expect(submitted.configuration.promptBlocks).not.toBe(selected.promptBlocks);
    expect(buildTurnRequest('Ancora la stessa domanda.', selected, server, 'next-turn').configuration).toEqual(selected);
  });

  it('non permette di aggirare allowLive o usare profili inesistenti', () => {
    const enabled = liveConfigurationFor('inline');
    expect(() => validateConfiguration({ ...settings, mode: 'live' }, configuration)).toThrow('LIVE non è pronto');
    expect(() => validateConfiguration({ ...settings, modelProfileId: 'invented' }, enabled)).toThrow('non restituito');
    expect(() => validateConfiguration({ ...settings, mode: 'live' }, {
      ...enabled, models: enabled.models.map((model) => ({ ...model, configured: false })),
    })).toThrow('non è configurato');
    expect(() => validateConfiguration({ ...settings, agentModels: { router: 'invented' } }, enabled)).toThrow('non restituito');
    expect(() => validateConfiguration({ ...settings, agentModels: { Router: 'gpt5' } }, enabled)).toThrow('Agente non registrato');
    expect(() => validateConfiguration({ ...settings, maxModelCalls: 0 }, enabled)).toThrow();
  });

  it.each(['inline', 'skills'] as const)('%s rifiuta override modello per servizi che non eseguono agenti', (technology) => {
    const server = liveConfigurationFor(technology);
    expect(validateConfiguration({ ...settings, approvedBudgetUsd: 0.1, agentModels: { router: 'gpt6-luna' } }, server).agentModels).toEqual({ router: 'gpt6-luna' });
    for (const agent of ['catalog', 'orders', 'returns']) {
      expect(() => buildTurnRequest('Ordine sintetico', { ...settings, approvedBudgetUsd: 0.1, agentModels: { [agent]: 'gpt5' } }, server, 'same-id'))
        .toThrow('Agente non registrato per questa demo');
    }
  });

  it('richiede liveReady e budget per il modello base e gli override, senza indovinare il pricing', () => {
    const server = liveConfigurationFor('a2a');
    const live = { ...settings, mode: 'live' as const, approvedBudgetUsd: 0.1 };
    expect(validateConfiguration(live, server)).toEqual(live);
    expect(() => validateConfiguration({ ...live, approvedBudgetUsd: null }, server)).toThrow('budget approvato');
    expect(() => validateConfiguration({ ...live, approvedBudgetUsd: 0.11 }, server)).toThrow('supera il limite');
    const blocked = { ...server, capabilities: { ...server.capabilities, modelCapabilities:
      server.models.map((model) => ({ modelProfileId: model.id, liveReady: model.id !== 'gpt6-sol' })) } };
    expect(() => validateConfiguration({ ...live, modelProfileId: 'gpt6-sol' }, blocked)).toThrow('LIVE non pronto');
    expect(() => validateConfiguration({ ...live, agentModels: { catalog: 'gpt6-sol' } }, blocked)).toThrow('LIVE non pronto');
    expect(() => validateConfiguration(live, { ...server, capabilities: { ...server.capabilities, modelCapabilities: undefined } })).toThrow('LIVE non pronto');
  });

  it('preserva readiness, budget e pricing a fasce ricevuti dal backend', async () => {
    const server = liveConfigurationFor('inline');
    const model = server.models[0];
    if (!model) throw new Error('Missing model fixture');
    const pricing = { ...model.pricing, cacheWritePerMillion: 2.5,
      longContextThresholdTokens: 272000, longContextInputPerMillion: 4, longContextCachedInputPerMillion: 0.4,
      longContextCacheWritePerMillion: 5, longContextOutputPerMillion: 15 };
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({
      ...server, models: [{ ...model, pricing }],
    })));
    const result = await new ObservatoryApi('inline').config();
    expect(result.capabilities).toEqual(server.capabilities);
    expect(result.models[0]?.pricing).toEqual(pricing);
  });

  it('accetta gli override A2A senza confondere agenti di esecuzione e servizi HTTP', () => {
    const agentModels = { router: 'gpt5', catalog: 'gpt6-astra', orders: 'gpt6-sol', returns: 'gpt6-luna' };
    expect(validateConfiguration({ ...settings, approvedBudgetUsd: 0.1, agentModels }, liveConfigurationFor('a2a')).agentModels).toEqual(agentModels);
  });

  it.each(['inline', 'skills', 'a2a'] as const)('preserva il contratto di topologia dichiarato da /config per %s', async (technology) => {
    const server = configurationFor(technology);
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(server)));
    expect((await new ObservatoryApi(technology).config()).capabilities).toEqual(server.capabilities);
  });

  it('non inventa quattro agenti quando /config omette le capability', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({ ...configuration, capabilities: undefined })));
    await expect(new ObservatoryApi('inline').config()).rejects.toThrow('capabilities');
  });

  it.each(['inline', 'skills', 'a2a'] as const)('richiede l’anteprima %s solo al suo endpoint con configurazione completa e cattura browser', async (technology) => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(promptPreview(technology)));
    vi.stubGlobal('fetch', fetchMock);
    const captures: HttpCapture[] = [];
    const api = new ObservatoryApi(technology, (entry) => captures.push(entry));
    const selected = { ...settings, promptProfile: 'gpt6' as const, promptBlocks: { ...settings.promptBlocks, checklist: true, examples: true } };
    const result = await api.previewPrompt(selected, configurationFor(technology));
    expect(result).toEqual(promptPreview(technology));
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(`/api/${technology}/prompts/preview`, expect.objectContaining({
      method: 'POST', body: JSON.stringify(selected), headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    }));
    expect(captures[0]?.request).toEqual(selected);
    expect(captures[0]?.response).toEqual(promptPreview(technology));
  });

  it('consente preview LIVE senza deployment/budget/autorizzazione ma non indebolisce i controlli di esecuzione', async () => {
    const live = { ...settings, mode: 'live' as const };
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(promptPreview()));
    vi.stubGlobal('fetch', fetchMock);
    expect(validatePreviewConfiguration(live, configuration)).toEqual(live);
    await expect(new ObservatoryApi('inline').previewPrompt(live, configuration)).resolves.toEqual(promptPreview());
    expect(() => validateConfiguration(live, configuration)).toThrow('LIVE non è pronto');
    expect(() => validateConfiguration(live, { ...configuration, allowLive: true })).toThrow('non è configurato');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('non chiama nemmeno la preview se configurazione, agente o blocco non sono supportati', async () => {
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    const api = new ObservatoryApi('inline');
    await expect(api.previewPrompt({ ...settings, modelProfileId: 'invented' }, configuration)).rejects.toThrow('non restituito');
    await expect(api.previewPrompt({ ...settings, agentModels: { catalog: 'gpt5' } }, configuration)).rejects.toThrow('Agente non registrato');
    const unsupported = { ...settings, promptBlocks: { ...settings.promptBlocks, examples: true } };
    await expect(api.previewPrompt(unsupported, { ...configuration, promptBlocks: [] })).rejects.toThrow('Blocco prompt non abilitato');
    expect(() => buildTurnRequest('Messaggio', unsupported, { ...liveConfigurationFor('inline'), promptBlocks: [] }, 'key')).toThrow('Blocco prompt non abilitato');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    { ...promptPreview(), technology: 'skills' },
    { ...promptPreview(), agents: [] },
    { ...promptPreview(), agents: [promptPreview().agents[0], promptPreview().agents[0]] },
    { ...promptPreview(), agents: [{ agent: 'orders', instructions: 'Other', characterCount: 5 }] },
    { ...promptPreview(), agents: [{ agent: 'router', instructions: 'Bad count', characterCount: -1 }] },
  ])('rifiuta anteprime con contratto o provenienza errati (%j)', async (response) => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(response)));
    await expect(new ObservatoryApi('inline').previewPrompt(settings, configuration)).rejects.toThrow();
  });

  it('non accetta un’A2A preview priva degli agenti remoti', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({ ...promptPreview(), technology: 'a2a' })));
    await expect(new ObservatoryApi('a2a').previewPrompt(settings, configurationFor('a2a'))).rejects.toThrow('esattamente gli agenti attivi');
  });

  it('propaga abort alla preview senza retry o catture tardive', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
    }));
    vi.stubGlobal('fetch', fetchMock);
    const captures: HttpCapture[] = [];
    const controller = new AbortController();
    const request = new ObservatoryApi('inline', (capture) => captures.push(capture)).previewPrompt(settings, configuration, controller.signal);
    const assertion = expect(request).rejects.toMatchObject({ name: 'AbortError' });
    controller.abort();
    await assertion;
    expect(fetchMock.mock.calls[0]?.[1]?.signal?.aborted).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(captures).toHaveLength(0);
  });

  it('usa soltanto URL same-origin e conserva il vero payload nell’inspector', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({
      runId: 'run-1', conversationId: 'conversation-1', eventsUrl: 'http://internal-service/api/runs/run-1/events',
      apiKey: 'unexpected-secret',
    }, 202));
    vi.stubGlobal('fetch', fetchMock);
    const captures: HttpCapture[] = [];
    const api = new ObservatoryApi('inline', (entry) => captures.push(entry));
    const request = buildTurnRequest('Prodotto #83', { ...settings, approvedBudgetUsd: 0.1 }, liveConfigurationFor('inline'), 'same-key');
    await api.submitTurn('conversation-1', request);
    expect(fetchMock).toHaveBeenCalledWith('/api/inline/conversations/conversation-1/turns', expect.objectContaining({
      method: 'POST', body: JSON.stringify(request),
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    }));
    expect(captures[0]?.request).toEqual(request);
    expect(captures[0]?.status).toBe(202);
    expect(JSON.stringify(captures)).not.toContain('unexpected-secret');
  });

  it('non sostituisce un contratto non valido con fixture', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({ products: [product] })));
    const api = new ObservatoryApi('skills');
    await expect(api.products()).rejects.toThrow('Contratto API non valido');
  });

  it('rileva HTML al posto di JSON e backend di tecnologia errata', async () => {
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(new Response('<html>Vite fallback</html>'))
      .mockResolvedValueOnce(jsonResponse(configuration));
    vi.stubGlobal('fetch', fetchMock);
    const api = new ObservatoryApi('a2a');
    await expect(api.config()).rejects.toThrow('Risposta non JSON');
    await expect(api.config()).rejects.toThrow('Proxy errato');
  });

  it('espone errori HTTP senza retry silenzioso', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse({ message: 'Backend non pronto' }, 503));
    vi.stubGlobal('fetch', fetchMock);
    await expect(new ObservatoryApi('inline').runs()).rejects.toThrow('Backend non pronto');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('interrompe richieste senza risposta con errore esplicito e nessun retry automatico', async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn<typeof fetch>().mockImplementation((_input, init) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
    }));
    vi.stubGlobal('fetch', fetchMock);
    const request = new ObservatoryApi('inline').runs();
    const assertion = expect(request).rejects.toThrow('Tempo massimo di risposta superato');
    await vi.advanceTimersByTimeAsync(30_001);
    await assertion;
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('il POST replay legge SSE originale con provenienza verificata e senza creare run', async () => {
    const original = event({ message: 'Registrazione originale LIVE', data: { mode: 'live' } });
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(`id: 1\ndata: ${JSON.stringify(original)}\n\n`, {
      headers: { 'Content-Type': 'text/event-stream; charset=utf-8', 'X-Original-Run-Id': 'run-1', 'X-Replay-Only': 'true' },
    }));
    vi.stubGlobal('fetch', fetchMock);
    const capture: HttpCapture[] = [];
    const received = vi.fn();
    const result = await new ObservatoryApi('inline', (entry) => capture.push(entry)).replay('run-1', received);
    expect(result).toMatchObject({ originalRunId: 'run-1', replayOnly: true, events: [original] });
    expect(received).toHaveBeenCalledWith(original);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/inline/runs/run-1/replay', expect.objectContaining({
      method: 'POST', headers: { Accept: 'text/event-stream' },
    }));
    expect(fetchMock.mock.calls[0]?.[1]?.body).toBeUndefined();
    expect(capture[0]?.responseContentType).toBe('text/event-stream; charset=utf-8');
    expect(capture[0]?.response).toMatchObject({ originalRunId: 'run-1', replayOnly: true });
  });

  it('rifiuta un replay senza header originali e non effettua retry dopo stream invalido', async () => {
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(`data: ${JSON.stringify(event())}\n\n`, {
        headers: { 'Content-Type': 'text/event-stream' },
      }))
      .mockResolvedValueOnce(new Response(`data: ${JSON.stringify(event({ runId: 'another-run' }))}\n\n`, {
        headers: { 'Content-Type': 'text/event-stream', 'X-Original-Run-Id': 'run-1', 'X-Replay-Only': 'true' },
      }));
    vi.stubGlobal('fetch', fetchMock);
    const api = new ObservatoryApi('inline');
    await expect(api.replay('run-1')).rejects.toThrow('provenienza');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await expect(api.replay('run-1')).rejects.toThrow('altro run');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
