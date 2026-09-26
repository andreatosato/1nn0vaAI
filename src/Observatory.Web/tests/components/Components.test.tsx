import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ConfigurationPanel, initialSettings, reconcileSettings } from '../../src/components/ConfigurationPanel';
import { JsonBlock } from '../../src/components/Common';
import { ProductCard, ResultProducts } from '../../src/components/ProductCards';
import { ExperimentPanel } from '../../src/components/ExperimentPanel';
import { TraceView } from '../../src/components/TraceView';
import { configuration, configurationFor, event, jsonResponse, liveConfigurationFor, modelCall, product, run, scenario, settings } from '../support/fixtures';
import { ObservatoryApi } from '../../src/lib/api';

const inlineApi = new ObservatoryApi('inline');

describe('interfaccia accessibile e dati non eseguibili', () => {
  it('LIVE è disabilitato dal server e il nome modello proviene da config', () => {
    render(<ConfigurationPanel api={inlineApi} server={configuration} settings={settings} onChange={vi.fn()} disabled={false} />);
    expect(screen.getByRole('status')).toHaveTextContent('LIVE non è pronto');
    for (const option of screen.getAllByRole('option', { name: 'GPT-5 · non disponibile in LIVE' })) expect(option).toBeDisabled();
    expect(screen.getByRole('combobox', { name: 'Profilo modello' })).toHaveValue('gpt5');
    expect(screen.getAllByRole('option', { name: /GPT-6 Luna/ }).length).toBeGreaterThan(0);
    expect(initialSettings(configuration)?.mode).toBe('live');
    expect(initialSettings({ ...configuration, allowLive: true, defaultMode: 'live' })?.mode).toBe('live');
    expect(initialSettings({ ...configuration, allowLive: false, defaultMode: 'live' })?.mode).toBe('live');
  });

  it('serializza gli override con i nomi agente minuscoli richiesti dal runtime', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    render(<ConfigurationPanel api={inlineApi} server={liveConfigurationFor('inline')} settings={settings} onChange={change} disabled={false} />);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Router' }), 'gpt6-luna');
    expect(change).toHaveBeenCalledWith(expect.objectContaining({ agentModels: { router: 'gpt6-luna' } }));
  });

  it('disabilita i profili LIVE non pronti nel modello base e negli override', async () => {
    const server = liveConfigurationFor('a2a');
    server.capabilities.modelCapabilities = server.models.map((model) => ({ modelProfileId: model.id, liveReady: model.id === 'gpt5' }));
    render(<ConfigurationPanel api={inlineApi} server={server} settings={{ ...settings, mode: 'live' }} onChange={vi.fn()} disabled={false} />);
    await userEvent.setup().click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    for (const option of screen.getAllByRole('option', { name: 'GPT-6 Sol · non disponibile in LIVE' })) expect(option).toBeDisabled();
    for (const option of screen.getAllByRole('option', { name: 'GPT-5' })) expect(option).toBeEnabled();
    expect(screen.getByRole('spinbutton', { name: 'Budget approvato (USD)' })).toHaveAttribute('max', '0.1');
  });

  it('offre senza limiti solo con opt-in e disabilita i limiti ignorati senza inviare richieste', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    const server = liveConfigurationFor('inline');
    const selected = { ...settings, approvedBudgetUsd: 0.1 };
    const { rerender } = render(<ConfigurationPanel api={inlineApi} server={server} settings={selected} onChange={change} disabled={false} />);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    expect(screen.queryByRole('checkbox', { name: /Senza limiti applicativi/ })).not.toBeInTheDocument();
    const enabledServer = { ...server, capabilities: { ...server.capabilities, allowUnboundedExecution: true } };
    rerender(<ConfigurationPanel api={inlineApi} server={enabledServer} settings={selected} onChange={change} disabled={false} />);
    await user.click(screen.getByRole('checkbox', { name: /Senza limiti applicativi/ }));
    const unbounded = { ...selected, unboundedExecution: true, approvedBudgetUsd: null };
    expect(change).toHaveBeenLastCalledWith(unbounded);
    rerender(<ConfigurationPanel api={inlineApi} server={enabledServer} settings={unbounded} onChange={change} disabled={false} />);
    for (const name of ['Limite token di output', 'Limite chiamate modello', 'Budget approvato (USD)'])
      expect(screen.getByRole('spinbutton', { name })).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: /Senza limiti applicativi/ }));
    expect(change).toHaveBeenLastCalledWith({ ...unbounded, unboundedExecution: false });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('spiega il runtime LIVE e le scelte indipendenti e rende visibili gli override senza cancellarli implicitamente', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    const selected = { ...settings, modelProfileId: 'gpt6-astra', agentModels: { router: 'gpt5' }, confirmAction: true };
    render(<ConfigurationPanel api={inlineApi} server={configuration} settings={selected} onChange={change} disabled={false} />);
    expect(screen.getByText('Il bot usa le nuove impostazioni dal prossimo “Invia”.')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Profilo modello' })).toHaveAccessibleDescription(/Modello e prompt sono scelte indipendenti/);
    expect(screen.getByText(/Router: GPT-5\. Queste scelte prevalgono/)).toBeInTheDocument();
    expect(change).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Usa il profilo modello per tutti gli agenti' }));
    expect(change).toHaveBeenCalledExactlyOnceWith({ ...selected, agentModels: {}, confirmAction: false });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('non applica modifiche mentre le impostazioni sono bloccate, neppure da un evento programmatico', () => {
    const change = vi.fn();
    render(<ConfigurationPanel api={inlineApi} server={configuration} settings={settings} onChange={change} disabled />);
    fireEvent.change(screen.getByRole('combobox', { name: 'Profilo modello' }), { target: { value: 'gpt6-astra' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Profilo prompt' }), { target: { value: 'bad' } });
    expect(change).not.toHaveBeenCalled();
    expect(screen.getByText(/Un retry riutilizza il payload già inviato/)).toBeInTheDocument();
  });

  it.each(['inline', 'skills'] as const)('%s configura soltanto il Router, non i servizi business', async (technology) => {
    const user = userEvent.setup();
    render(<ConfigurationPanel api={new ObservatoryApi(technology)} server={configurationFor(technology)} settings={settings} onChange={vi.fn()} disabled={false} />);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    expect(screen.getByRole('combobox', { name: 'Router' })).toBeInTheDocument();
    for (const name of ['Catalog', 'Orders', 'Returns']) expect(screen.queryByRole('combobox', { name })).not.toBeInTheDocument();
    expect(screen.getByText(/Solo il Router esegue il modello/)).toBeInTheDocument();
  });

  it('A2A configura gli agenti remoti e rispetta gli agenti abilitati da capabilities', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    const server = liveConfigurationFor('a2a');
    const { rerender } = render(<ConfigurationPanel api={new ObservatoryApi('a2a')} server={server} settings={settings} onChange={change} disabled={false} />);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    for (const name of ['Router', 'Catalog', 'Orders', 'Returns']) expect(screen.getByRole('combobox', { name })).toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox', { name: 'Returns' }), 'gpt6-sol');
    expect(change).toHaveBeenLastCalledWith(expect.objectContaining({ agentModels: { returns: 'gpt6-sol' } }));
    rerender(<ConfigurationPanel api={new ObservatoryApi('a2a')} server={{ ...server, capabilities: { ...server.capabilities, agentNames: ['router', 'catalog'] } }} settings={settings} onChange={change} disabled={false} />);
    expect(screen.getByRole('combobox', { name: 'Catalog' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Orders' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Returns' })).not.toBeInTheDocument();
  });

  it('rimuove override non eseguibili senza alterare profili, history, limiti o budget', () => {
    const previous = {
      ...settings, modelProfileId: 'gpt6-astra', promptProfile: 'gpt6' as const, historyStrategy: 'compact' as const,
      agentModels: { router: 'gpt6-luna', catalog: 'gpt6-sol', orders: 'gpt5', returns: 'gpt5' },
      promptBlocks: { ...settings.promptBlocks, checklist: true, examples: true },
      maxModelCalls: 12, maxOutputTokens: 2000, approvedBudgetUsd: 0.25, confirmAction: true,
    };
    expect(reconcileSettings(previous, configurationFor('a2a'))).toEqual(previous);
    expect(reconcileSettings(previous, configurationFor('skills'))).toEqual({
      ...previous, agentModels: { router: 'gpt6-luna' }, confirmAction: false,
    });
    expect(reconcileSettings({ ...previous, agentModels: { router: 'removed-profile' } }, configuration)?.agentModels).toEqual({});
    expect(previous.agentModels).toHaveProperty('returns', 'gpt5');
  });

  it('un cambio di capability non reintroduce override nascosti al prossimo aggiornamento', async () => {
    const user = userEvent.setup();
    const change = vi.fn();
    const stale = { ...settings, agentModels: { router: 'gpt5', catalog: 'gpt6-sol', returns: 'gpt6-luna' } };
    const { rerender } = render(<ConfigurationPanel api={new ObservatoryApi('a2a')} server={liveConfigurationFor('a2a')} settings={stale} onChange={change} disabled={false} />);
    await user.click(screen.getByText('Modelli per agente, limiti ed esempi di prompt'));
    rerender(<ConfigurationPanel api={inlineApi} server={liveConfigurationFor('inline')} settings={stale} onChange={change} disabled={false} />);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Router' }), 'gpt6-astra');
    expect(change).toHaveBeenLastCalledWith(expect.objectContaining({ agentModels: { router: 'gpt6-astra' } }));
  });

  it('mostra JSON in pre, non HTML, e oscura segreti anche inattesi', () => {
    const { container } = render(<JsonBlock value={{
      text: '<img src=x onerror="alert(1)">', access_token: 'do-not-show-me',
    }} label="Richiesta reale" />);
    expect(container.querySelector('pre')).toHaveTextContent('<img src=x onerror=');
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('pre')).not.toHaveTextContent('do-not-show-me');
    expect(container.querySelector('pre')).toHaveTextContent('[REDACTED]');
  });

  it('le card usano ID backend e immagini solo UI, con fallback esplicito se la rete fallisce', async () => {
    const user = userEvent.setup();
    const ask = vi.fn();
    render(<ResultProducts ids={[83, 83, 999]} products={[product]} onAsk={ask} />);
    const image = screen.getByRole('img', { name: product.title });
    expect(image).toHaveAttribute('src', product.thumbnail);
    expect(screen.getAllByRole('article')).toHaveLength(1);
    await user.click(screen.getByRole('button', { name: /Prepara una domanda/ }));
    expect(ask).toHaveBeenCalledWith(product);
    fireEvent.error(image);
    expect(screen.getByText('Immagine non disponibile')).toBeInTheDocument();
    expect(screen.getByText(/#999/)).toBeInTheDocument();
  });

  it('non carica URL immagine non pubblici restituiti accidentalmente dal backend', () => {
    render(<ProductCard product={{ ...product, thumbnail: 'http://internal-service/private-image' }} />);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByText('Immagine non disponibile')).toBeInTheDocument();
  });

  it('mantiene visibile uno scostamento temporale negativo invece di inventare uno zero', () => {
    render(<TraceView run={run()} events={[]} calls={[modelCall({ startedAt: '2026-09-23T09:59:59.980Z' })]} />);
    expect(screen.getByText('inizio -20 ms')).toBeInTheDocument();
  });

  it('distingue il servizio HTTP dall’agente e non crea righe modello per le chiamate business', () => {
    render(<TraceView run={run()} events={[event({
      kind: 'protocol.request', agent: 'router', data: { protocol: 'HTTP', service: 'orders', operation: 'get_order' },
    })]} calls={[modelCall()]} />);
    expect(screen.getByText('Agente: router')).toBeInTheDocument();
    expect(screen.getByText('Servizio: orders')).toBeInTheDocument();
    expect(screen.getByText('HTTP')).toBeInTheDocument();
    const waterfall = screen.getByLabelText('Durata e posizione temporale delle chiamate modello');
    expect(waterfall.querySelectorAll('.waterfall-row')).toHaveLength(1);
    expect(waterfall).not.toHaveTextContent('orders');
  });

  it('esegue il dry run solo al click e mostra qualunque risposta JSON reale', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => jsonResponse({
      customPlanFromBackend: { providerCalls: 'not-executed', secret: 'hidden-plan-secret' },
    }));
    vi.stubGlobal('fetch', fetchMock);
    const experimentSettings = { ...settings, promptBlocks: { ...settings.promptBlocks, checklist: true, examples: true } };
    render(<ExperimentPanel api={new ObservatoryApi('inline')} server={configuration} scenarios={[scenario]} settings={experimentSettings} disabled={false} onCompleted={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /GPT-5/ })).toBeChecked());
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Calcola piano · dry run' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Astra / Sol ↔ Luna' }));
    await user.click(screen.getByRole('button', { name: /Calcola piano/ }));
    await screen.findByRole('heading', { name: 'Piano restituito dal backend' });
    const sentBody = fetchMock.mock.calls[0]?.[1]?.body;
    expect(typeof sentBody).toBe('string');
    const sent: unknown = typeof sentBody === 'string' ? JSON.parse(sentBody) : null;
    expect(sent).toMatchObject({
      scenarioIds: ['main-six-turns'], repetitions: 1, dryRun: true,
      configurations: [
        { modelProfileId: 'gpt6-astra', mode: 'live', confirmAction: false, agentModels: {}, promptBlocks: experimentSettings.promptBlocks },
        { modelProfileId: 'gpt6-sol', mode: 'live', confirmAction: false, agentModels: {}, promptBlocks: experimentSettings.promptBlocks },
        { modelProfileId: 'gpt6-luna', mode: 'live', confirmAction: false, agentModels: {}, promptBlocks: experimentSettings.promptBlocks },
      ],
    });
    expect(screen.getByText(/"customPlanFromBackend"/)).toBeInTheDocument();
    expect(screen.queryByText(/hidden-plan-secret/)).not.toBeInTheDocument();
  });
});
