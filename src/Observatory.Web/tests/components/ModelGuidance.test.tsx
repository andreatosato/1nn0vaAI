import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { modelPromptGuidance } from '../../src/modelGuidance';
import { ModelGuidance } from '../../src/components/ModelGuidance';

describe('consigli documentali separati dai modelli eseguibili', () => {
  it('offre tutte le famiglie e fonti OpenAI verificate, senza chiamate di rete', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    render(<ModelGuidance />);
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['GPT-6', 'GPT-5.6', 'GPT-5.4', 'GPT-5']);
    for (const guide of modelPromptGuidance) {
      const tab = screen.getByRole('tab', { name: guide.family });
      await user.click(tab);
      const panel = screen.getByRole('tabpanel', { name: guide.family });
      expect(tab).toHaveAttribute('aria-selected', 'true');
      expect(tab).toHaveAttribute('aria-controls', panel.id);
      expect(panel).toHaveAttribute('aria-labelledby', tab.id);
      const source = within(panel).getByRole('link', { name: /^Fonte OpenAI:/ });
      expect(source).toHaveAttribute('href', guide.source.url);
      expect(new URL(source.getAttribute('href') ?? '').hostname).toBe('developers.openai.com');
      expect(source).toHaveAttribute('rel', 'noopener noreferrer');
      expect(source).toHaveAttribute('target', '_blank');
    }
    expect(screen.getByText(/24 settembre 2026/)).toBeInTheDocument();
    expect(screen.getByText(/Confronta gli stessi scenari e verifica i risultati nelle tracce/)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('permette navigazione da tastiera con frecce, Home ed End e un solo tab attivo', async () => {
    const user = userEvent.setup();
    render(<ModelGuidance />);
    await user.click(screen.getByRole('tab', { name: 'GPT-6' }));
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'GPT-5.6' })).toHaveFocus();
    expect(screen.getByRole('tabpanel', { name: 'GPT-5.6' })).toBeInTheDocument();
    await user.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'GPT-5' })).toHaveFocus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'GPT-6' })).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tab', { name: 'GPT-5' })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(screen.getByRole('tab', { name: 'GPT-6' })).toHaveFocus();
    expect(screen.getAllByRole('tab').filter((tab) => tab.tabIndex === 0)).toHaveLength(1);
    expect(screen.getAllByRole('tabpanel')).toHaveLength(1);
  });

  it('non trasferisce le raccomandazioni Astra agli alias Sol/Luna della demo', () => {
    render(<ModelGuidance />);
    const panel = screen.getByRole('tabpanel', { name: 'GPT-6' });
    expect(within(panel).getByText(/Non sono verificate per gli alias Sol\/Luna della demo/)).toBeInTheDocument();
    expect(panel).toHaveTextContent('non presumere comportamento o risultati equivalenti');
  });
});
