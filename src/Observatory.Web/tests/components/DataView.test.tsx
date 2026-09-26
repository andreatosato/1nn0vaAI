import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ObservatoryApi } from '../../src/lib/api';
import { demoData, jsonResponse, pathOf, product } from '../support/fixtures';
import { DataView } from '../../src/components/DataView';

function setup() {
  const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () => jsonResponse(demoData));
  vi.stubGlobal('fetch', fetchMock);
  const ask = vi.fn();
  const reload = vi.fn();
  render(<DataView api={new ObservatoryApi('inline')}
    products={{ data: [product], loading: false, error: null, reload }} onAsk={ask} />);
  return { fetchMock, ask, reload, user: userEvent.setup() };
}

describe('pagina dati in sola lettura', () => {
  it('espone dettagli prodotto completi e ricerca anche marca, SKU e tag, senza invii', async () => {
    const { user, ask, fetchMock } = setup();
    await screen.findByText(/Cliente della chat:/);
    await user.click(screen.getByText('Dettagli completi · #83'));
    const details = screen.getByText('Dettagli completi · #83').closest('details');
    if (!details) throw new Error('Dettagli prodotto mancanti');
    expect(within(details).getByText(product.description)).toBeVisible();
    expect(screen.getByText('Test catalog')).toBeVisible();
    expect(screen.getByText('TEST-83')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Immagine 1' })).toHaveAttribute('href', product.images[0]);
    const search = screen.getByRole('searchbox', { name: 'Cerca nel catalogo caricato' });
    for (const term of ['Test catalog', 'TEST-83', 'shirt']) {
      await user.clear(search); await user.type(search, term);
      expect(screen.getAllByRole('article')).toHaveLength(1);
    }
    expect(ask).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const request = fetchMock.mock.calls[0];
    if (!request) throw new Error('Lettura dati mancante');
    expect(pathOf(request[0])).toBe('/api/inline/demo-data');
    expect(fetchMock.mock.calls[0]?.[1]?.method).toBe('GET');
  });

  it('mostra ogni campo ordine, filtra cliente e tracking e prepara solo una domanda testuale', async () => {
    const { user, ask } = setup();
    await screen.findByRole('button', { name: 'Ordini (2)' });
    await user.click(screen.getByRole('button', { name: 'Ordini (2)' }));
    const row = screen.getByRole('rowheader', { name: /ORD-1042/ }).closest('tr');
    expect(row).toHaveTextContent('CUST-DEMO-01');
    expect(row).toHaveTextContent('#83');
    expect(row).toHaveTextContent(product.title);
    expect(row).toHaveTextContent('29,99 USD');
    expect(row).toHaveTextContent('19,99 USD');
    expect(row).toHaveTextContent('Sì');
    expect(row).toHaveTextContent('2026-09-05');
    expect(row).toHaveTextContent('delivered');
    expect(row).toHaveTextContent('TRACK-1042');
    expect(screen.getByText('Altro cliente: non accessibile al bot')).toBeVisible();
    await user.click(screen.getByRole('checkbox', { name: 'Solo ordini del cliente della chat' }));
    expect(screen.queryByRole('rowheader', { name: /ORD-1001/ })).not.toBeInTheDocument();
    expect(screen.getByText('1 di 2 ordini')).toBeVisible();
    await user.click(screen.getByRole('checkbox', { name: 'Solo ordini del cliente della chat' }));
    await user.type(screen.getByRole('searchbox', { name: 'Cerca negli ordini' }), 'TRACK-1001');
    expect(screen.queryByRole('rowheader', { name: /ORD-1042/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: "Prepara una domanda sull'ordine ORD-1001" }));
    expect(ask).toHaveBeenCalledExactlyOnceWith("Mostra l'ordine ORD-1001.");
  });

  it('mostra testi, identificativi, priorità e versioni di tutte le policy', async () => {
    const { user, ask } = setup();
    await user.click(await screen.findByRole('button', { name: 'Policy (2)' }));
    const policies = screen.getAllByRole('article');
    expect(policies).toHaveLength(2);
    for (const [index, policy] of demoData.policies.entries()) {
      const article = policies[index];
      if (!article) throw new Error('Policy non renderizzata');
      expect(within(article).getByRole('heading')).toHaveTextContent(policy.title);
      expect(article).toHaveTextContent(policy.text);
      expect(article).toHaveTextContent(`${policy.id} · Priorità ${policy.priority} · Versione ${policy.version}`);
    }
    expect(ask).not.toHaveBeenCalled();
  });

  it('rifiuta risposte malformate e rende espliciti errore e retry senza simulare un dataset vuoto', async () => {
    const { user, fetchMock } = setup();
    await screen.findByRole('button', { name: 'Ordini (2)' });
    fetchMock.mockImplementationOnce(async () => jsonResponse({}));
    await user.click(screen.getByRole('button', { name: 'Aggiorna dati' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Contratto API non valido');
    expect(screen.queryByRole('button', { name: 'Ordini (0)' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Riprova' }));
    await screen.findByRole('button', { name: 'Ordini (2)' });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
  });

  it('non presenta vecchi ordini come aggiornati dopo un refresh fallito', async () => {
    const { user, fetchMock, reload } = setup();
    await user.click(await screen.findByRole('button', { name: 'Ordini (2)' }));
    fetchMock.mockImplementationOnce(async () => jsonResponse({ message: 'Servizio Orders non disponibile' }, 503));
    await user.click(screen.getByRole('button', { name: 'Aggiorna dati' }));
    await screen.findByRole('alert');
    expect(screen.queryByRole('rowheader', { name: /ORD-1042/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Nessun ordine corrisponde/)).not.toBeInTheDocument();
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('distingue collezioni vuote valide da errori e filtri senza risultati', async () => {
    const { user, fetchMock } = setup();
    await user.click(await screen.findByRole('button', { name: 'Ordini (2)' }));
    await user.type(screen.getByRole('searchbox', { name: 'Cerca negli ordini' }), 'inesistente');
    expect(screen.getByText('Nessun ordine corrisponde ai filtri.')).toBeVisible();
    fetchMock.mockImplementationOnce(async () => jsonResponse({ ...demoData, orders: [], policies: [] }));
    await user.click(screen.getByRole('button', { name: 'Aggiorna dati' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ordini (0)' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Policy (0)' }));
    expect(screen.getByText('Nessuna policy presente.')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
