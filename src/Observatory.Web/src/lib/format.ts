const number = new Intl.NumberFormat('it-IT');
const precise = new Intl.NumberFormat('it-IT', { maximumFractionDigits: 1 });

export function tokens(value: number | null | undefined): string {
  return value === null || value === undefined
    ? 'Non disponibile'
    : number.format(value);
}

export function money(value: number | null | undefined, currency = 'USD'): string {
  if (value === null || value === undefined) return 'Non disponibile';
  try {
    const formatter = new Intl.NumberFormat('it-IT', {
      style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 6,
    });
    return value > 0 && value < 0.000001 ? `< ${formatter.format(0.000001)}` : formatter.format(value);
  } catch {
    return `${number.format(value)} ${currency}`;
  }
}

export function duration(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'Non disponibile';
  return value >= 1000 ? `${precise.format(value / 1000)} s` : `${precise.format(value)} ms`;
}

export function dateTime(value: string): string {
  return new Intl.DateTimeFormat('it-IT', { dateStyle: 'short', timeStyle: 'medium' }).format(new Date(value));
}

export function shortId(value: string): string {
  return value.length > 14 ? `${value.slice(0, 8)}…${value.slice(-4)}` : value;
}

export function isTerminal(status: string | null | undefined): boolean {
  return status !== undefined && status !== null &&
    ['completed', 'failed', 'cancelled', 'canceled', 'replayed'].includes(status.toLowerCase());
}

export function statusLabel(status: string): string {
  const labels: Record<string, string> = {
    queued: 'In coda', running: 'In esecuzione', completed: 'Completato',
    failed: 'Fallito', cancelling: 'Annullamento in corso', cancelled: 'Annullato',
    canceled: 'Annullato', replayed: 'Replay registrato',
  };
  return labels[status] ?? status;
}

export function publicThumbnail(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    const allowed = ['cdn.dummyjson.com', 'dummyjson.com', 'i.dummyjson.com'];
    return url.protocol === 'https:' && allowed.includes(url.hostname) && !url.username && !url.password && !url.search && !url.hash
      ? url.href : null;
  } catch {
    return null;
  }
}
