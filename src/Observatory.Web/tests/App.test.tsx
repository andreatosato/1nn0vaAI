import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App, { parseRoute } from '../src/App';
import { configurationFor, jsonResponse, pathOf } from './support/fixtures';

function backend() {
  vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockImplementation(async (input) => {
    const path = pathOf(input);
    if (path === '/api/inline/config') return jsonResponse({ ...configurationFor('inline'), allowLive: true,
      capabilities: { ...configurationFor('inline').capabilities, allowUnboundedExecution: true } });
    if (path === '/api/inline/products') return jsonResponse([]);
    if (path === '/api/inline/scenarios') return jsonResponse([]);
    if (path === '/api/inline/conversations') return jsonResponse([]);
    if (path === '/api/inline/runs') return jsonResponse([]);
    throw new Error(`Unexpected request: ${path}`);
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('chat essenziale', () => {
  it('mantiene le route del laboratorio e rifiuta la pagina Inspector rimossa', () => {
    expect(parseRoute('#/')).toEqual({ kind: 'home' });
    expect(parseRoute('#/skills/chat')).toEqual({ kind: 'demo', technology: 'skills', page: 'chat' });
    expect(parseRoute('#/inline/trace')).toEqual({ kind: 'demo', technology: 'inline', page: 'trace' });
    expect(parseRoute('#/inline/inspector')).toEqual({ kind: 'not-found' });
  });

  it('mantiene la configurazione della demo e prepara la domanda nel pannello chat', async () => {
    backend();
    window.history.replaceState(null, '', '/#/inline/chat');
    render(<App />);
    expect(await screen.findByRole('combobox', { name: 'Profilo modello' })).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'Pagine del laboratorio' })).toBeVisible();
  });
});
