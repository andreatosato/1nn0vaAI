import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { Technology } from '../../src/contracts';
import { demoMetadata } from '../../src/metadata';
import { ChatWidget } from '../../src/components/ChatWidget';

function Content({ disabled = false }: { disabled?: boolean }) {
  const [draft, setDraft] = useState('');
  return <>
    <label>Messaggio di prova<textarea disabled={disabled} value={draft} onChange={(event) => setDraft(event.target.value)} /></label>
    <button type="button">Controllo nel pannello</button>
  </>;
}

function Harness({ technology = 'inline', disabled = false, focusRequest = 0, status = 'Parla con il Router' }: {
  technology?: Technology; disabled?: boolean; focusRequest?: number; status?: string;
}) {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button">Controllo della pagina</button>
    <ChatWidget technology={technology} open={open} onOpenChange={setOpen} focusRequest={focusRequest} mode="live" status={status}>
      <Content disabled={disabled} />
    </ChatWidget>
  </>;
}

describe('dimensioni del widget con scrollbar classica', () => {
  const styles = readFileSync(resolve('src', 'styles.css'), 'utf8');
  const widgetRules = [...styles.matchAll(/\.chat-widget\s*\{([^}]+)\}/g)].map((match) => match[1] ?? '');

  it('limita la larghezza alla viewport di layout, non a 100vw che include la scrollbar', () => {
    expect(widgetRules).toHaveLength(2);
    expect(widgetRules[0]).toMatch(/width:\s*min\(var\(--chat-panel-width\),\s*calc\(100%\s*-/);
    for (const rule of widgetRules) expect(rule).not.toMatch(/\d+(?:d|s|l)?vw\b/);
  });

  it('su mobile lascia entrambi i margini da 8px e calcola automaticamente lo spazio rimanente', () => {
    const mobileWidget = styles.slice(styles.lastIndexOf('@media (max-width: 640px)'))
      .match(/\.chat-widget\s*\{([^}]+)\}/)?.[1];
    expect(mobileWidget).toMatch(/left:\s*calc\(8px\s*\+\s*env\(safe-area-inset-left,\s*0px\)\)/);
    expect(mobileWidget).toMatch(/right:\s*calc\(8px\s*\+\s*env\(safe-area-inset-right,\s*0px\)\)/);
    expect(mobileWidget).toMatch(/width:\s*auto\s*;/);
  });
});

describe('widget bot non modale', () => {
  it.each(['inline', 'skills', 'a2a'] as const)('identifica %s e apre da tastiera senza richieste o contenuto duplicato', async (technology) => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    const { container } = render(<Harness technology={technology} />);
    const title = demoMetadata[technology].title;
    const launcher = screen.getByRole('button', { name: `Apri chat ${title}` });
    expect(launcher).toHaveAttribute('aria-haspopup', 'dialog');
    expect(launcher).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    launcher.focus();
    await user.keyboard('{Enter}');
    const panel = screen.getByRole('dialog', { name: `Chat con il Router · ${title}` });
    expect(panel).toHaveAttribute('id', launcher.getAttribute('aria-controls'));
    expect(panel).toHaveAttribute('aria-modal', 'false');
    expect(panel).toHaveAccessibleDescription(new RegExp(`Solo demo ${title}`));
    expect(launcher).toHaveAttribute('aria-expanded', 'true');
    expect(within(panel).getByRole('textbox')).toHaveFocus();
    expect(container.querySelectorAll('textarea')).toHaveLength(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('Escape, chiusura e riduzione ripristinano il focus senza smontare o cancellare la bozza', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    const composer = screen.getByRole('textbox');
    await user.type(composer, 'Bozza conservata, non inviata.');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    const launcher = screen.getByRole('button', { name: 'Apri chat Inline' });
    expect(launcher).toHaveFocus();
    expect(composer).toBeInTheDocument();
    expect(composer).not.toBeVisible();
    await user.keyboard(' ');
    expect(screen.getByRole('textbox')).toBe(composer);
    expect(composer).toHaveValue('Bozza conservata, non inviata.');
    await user.click(screen.getByRole('button', { name: 'Chiudi chat Inline' }));
    expect(launcher).toHaveFocus();
    await user.click(launcher);
    await user.click(screen.getByRole('button', { name: 'Riduci chat Inline' }));
    expect(launcher).toHaveFocus();
    expect(composer).toHaveValue('Bozza conservata, non inviata.');
  });

  it('da chiuso offre solo il launcher nel percorso Tab e rende inerte il contenuto conservato', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const launcher = screen.getByRole('button', { name: 'Apri chat Inline' });
    const panel = screen.getByRole('dialog', { hidden: true });
    expect(panel).toHaveAttribute('hidden');
    expect(panel).toHaveAttribute('inert');
    expect(panel).toHaveAttribute('aria-hidden', 'true');
    expect(screen.getAllByRole('button')).toHaveLength(2);
    screen.getByRole('button', { name: 'Controllo della pagina' }).focus();
    await user.tab();
    expect(launcher).toHaveFocus();
    await user.tab();
    expect(document.body).toHaveFocus();
    await user.tab({ shift: true });
    expect(launcher).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(panel).not.toHaveAttribute('hidden');
    expect(panel).not.toHaveAttribute('inert');
    expect(panel).toHaveAttribute('aria-hidden', 'false');
  });

  it('porta il focus al titolo se il compositore è disabilitato e permette Escape anche dopo perdita del focus', async () => {
    const user = userEvent.setup();
    render(<Harness disabled status="Turno in corso" />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    expect(screen.getByRole('heading', { name: 'Chat con il Router · Inline' })).toHaveFocus();
    fireEvent.keyDown(document.body, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apri chat Inline' })).toHaveFocus();
  });

  it('non intercetta Escape usato da una composizione IME o già gestito dal campo', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    const composer = screen.getByRole('textbox');
    fireEvent.keyDown(composer, { key: 'Escape', isComposing: true });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    const handled = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    handled.preventDefault();
    fireEvent(composer, handled);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('non intrappola Tab né oscura un controllo della pagina su schermi compatti', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }));
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    await user.type(screen.getByRole('textbox'), 'Bozza mobile');
    const pageControl = screen.getByRole('button', { name: 'Controllo della pagina' });
    await user.click(pageControl);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(pageControl).toHaveFocus();
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    expect(screen.getByRole('textbox')).toHaveValue('Bozza mobile');
  });

  it('su desktop mantiene la chat aperta mentre il focus passa alla pagina', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }));
    render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    await user.tab();
    expect(screen.getByRole('button', { name: 'Controllo nel pannello' })).toHaveFocus();
    const pageControl = screen.getByRole('button', { name: 'Controllo della pagina' });
    await user.click(pageControl);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(pageControl).toHaveFocus();
    fireEvent.keyDown(pageControl, { key: 'Escape' });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('aggiornare lo stato non ruba il focus; una richiesta prodotto lo riporta al messaggio', async () => {
    const user = userEvent.setup();
    const { rerender } = render(<Harness />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Inline' }));
    const control = screen.getByRole('button', { name: 'Controllo nel pannello' });
    await user.click(control);
    rerender(<Harness status="Turno in corso" />);
    expect(control).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Riduci chat Inline' })).toHaveAccessibleDescription('Turno in corso');
    rerender(<Harness status="Turno in corso" focusRequest={1} />);
    expect(screen.getByRole('textbox')).toHaveFocus();
  });

  it('offre un collegamento alla configurazione della stessa tecnologia', async () => {
    const user = userEvent.setup();
    render(<Harness technology="skills" />);
    await user.click(screen.getByRole('button', { name: 'Apri chat Agent Skills' }));
    const link = screen.getByRole('link', { name: 'Apri configurazione della demo Agent Skills' });
    expect(link).toHaveAttribute('href', '#/skills/chat');
    await user.click(link);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
