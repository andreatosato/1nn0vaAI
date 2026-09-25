import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, run } from '../test/fixtures';
import { MeasureComparisonView } from './MeasureComparisonView';

describe('confronto misure per richiesta identica', () => {
  it('confronta run registrati delle tre demo senza eseguire inferenze', async () => {
    const sameTextInline = run({
      id: 'inline-1', technology: 'inline', status: 'completed', message: 'Quante camicie rosse hai?',
      configuration: { ...run().configuration, mode: 'live', modelProfileId: 'gpt5' },
      startedAt: '2026-09-25T08:00:00Z', durationMs: 1200, timeToFirstAnswerMs: 500,
      inputTokens: 100, outputTokens: 25, estimatedCostUsd: 0.001, costStatus: 'priced',
    });
    const sameTextSkills = run({
      id: 'skills-1', technology: 'skills', status: 'completed', message: 'Quante   camicie rosse hai?',
      configuration: { ...run().configuration, mode: 'live', modelProfileId: 'gpt6-sol', promptProfile: 'good' },
      startedAt: '2026-09-25T08:01:00Z', durationMs: 2400, timeToFirstAnswerMs: 800,
      inputTokens: 160, outputTokens: 40, estimatedCostUsd: 0.002, costStatus: 'estimated',
    });
    const unrelated = run({
      id: 'a2a-1', technology: 'a2a', status: 'completed', message: 'Dov’è il mio ordine?',
      configuration: { ...run().configuration, mode: 'live' },
      startedAt: '2026-09-25T08:02:00Z', durationMs: 900,
      estimatedCostUsd: 0.0005, costStatus: 'priced',
    });
    const byTechnology = { inline: [sameTextInline], skills: [sameTextSkills], a2a: [unrelated] };
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(async (input) => {
      const path = new URL(String(input), window.location.origin).pathname;
      const technology = path.split('/')[2] as keyof typeof byTechnology;
      if (path === `/api/${technology}/runs`) return jsonResponse(byTechnology[technology]);
      throw new Error(`Unexpected GET ${path}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<MeasureComparisonView />);
    const table = await screen.findByRole('table', { name: 'Run della richiesta selezionata · ordinati dal meno al più recente' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByText('Inline')).toBeInTheDocument();
    expect(within(table).getByText('Agent Skills')).toBeInTheDocument();
    expect(within(table).getByText('gpt5')).toBeInTheDocument();
    expect(within(table).getByText('gpt6-sol')).toBeInTheDocument();
    expect(within(table).queryByText('Dov’è il mio ordine?')).not.toBeInTheDocument();
    expect(screen.getByText('Costo LIVE registrato più basso').parentElement).toHaveTextContent(/0,001/);
    expect(screen.getByText('Quante camicie rosse hai?', { selector: 'strong' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === undefined || init.method === 'GET')).toBe(true);
  });

  it('non presenta costi mancanti come misure monetarie e filtra la richiesta', async () => {
    const user = userEvent.setup();
    const fixtureRun = run({
      id: 'fixture-1', technology: 'inline', status: 'completed', message: 'Richiesta senza costo',
      durationMs: 300, timeToFirstAnswerMs: null, costStatus: 'unpriced',
    });
    const otherRun = run({
      id: 'skills-1', technology: 'skills', status: 'completed', message: 'Richiesta differente',
      configuration: { ...run().configuration, mode: 'live' }, durationMs: 400,
      costStatus: 'unpriced',
    });
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockImplementation(async (input) => {
      const path = new URL(String(input), window.location.origin).pathname;
      if (path === '/api/inline/runs') return jsonResponse([fixtureRun]);
      if (path === '/api/skills/runs') return jsonResponse([otherRun]);
      if (path === '/api/a2a/runs') return jsonResponse([]);
      throw new Error(`Unexpected GET ${path}`);
    }));

    render(<MeasureComparisonView />);
    await screen.findByRole('table', { name: 'Run della richiesta selezionata · ordinati dal meno al più recente' });
    const selector = screen.getByRole('combobox', { name: 'Richiesta identica' });
    await user.selectOptions(selector, 'Richiesta senza costo');
    const table = screen.getByRole('table', { name: 'Run della richiesta selezionata · ordinati dal meno al più recente' });
    expect(within(table).getAllByText('Non disponibile')).toHaveLength(3);
    expect(within(table).queryByText('Richiesta differente')).not.toBeInTheDocument();
    expect(screen.getByText('Richiesta senza costo', { selector: 'strong' })).toBeVisible();
  });
});
