import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { product } from '../support/fixtures';
import { CatalogView } from '../../src/components/CatalogView';

describe('catalogo separato dal widget', () => {
  it('filtra il catalogo già caricato e prepara una domanda soltanto al click', async () => {
    const user = userEvent.setup();
    const ask = vi.fn();
    const reload = vi.fn();
    render(<CatalogView products={{ data: [product, { ...product, id: 84, sku: 'TEST-84', title: 'Altro prodotto' }], error: null, loading: false, reload }} onAsk={ask} />);
    expect(screen.getAllByRole('article')).toHaveLength(2);
    expect(screen.queryByRole('textbox', { name: 'Messaggio al Router' })).not.toBeInTheDocument();
    await user.type(screen.getByRole('searchbox', { name: 'Cerca nel catalogo caricato' }), '83');
    expect(screen.getAllByRole('article')).toHaveLength(1);
    expect(ask).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: /Prepara una domanda/ }));
    expect(ask).toHaveBeenCalledExactlyOnceWith(product);
    expect(reload).not.toHaveBeenCalled();
  });

  it('mantiene caricamento, errori e retry esplicito senza inventare prodotti', async () => {
    const user = userEvent.setup();
    const reload = vi.fn();
    const { rerender } = render(<CatalogView products={{ data: null, loading: true, error: null, reload }} onAsk={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Caricamento prodotti');
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
    rerender(<CatalogView products={{ data: null, loading: false, error: 'Catalogo irraggiungibile', reload }} onAsk={vi.fn()} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Catalogo irraggiungibile');
    expect(reload).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Riprova' }));
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
