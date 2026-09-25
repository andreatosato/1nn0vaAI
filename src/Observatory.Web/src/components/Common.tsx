import { Component, useId, useState } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import type { RunEvent } from '../contracts';
import { protocolDetails } from '../lib/events';
import { errorMessage, redactText, safeJson } from '../lib/redaction';

const paths = {
  arrow: 'M5 12h14m-6-6 6 6-6 6',
  chat: 'M5 4h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9l-5 3V6a2 2 0 0 1 1-2Z',
  network: 'M9 3h6v6H9zM2 16h6v6H2zM16 16h6v6h-6zM12 9v4M5 16v-3h14v3',
  trace: 'M3 5h18M3 12h12M8 19h13M3 3v4M3 10v4M8 17v4',
  code: 'm8 5-6 7 6 7m8-14 6 7-6 7M14 3l-4 18',
  chart: 'M4 3v18h18M9 17v-5m5 5V7m5 10V4',
  history: 'M3 11a9 9 0 1 1 2 7M3 4v7h7m2-5v6l4 2',
  refresh: 'M20 8a8 8 0 0 0-14-3L3 8m0-6v6h6m-5 8a8 8 0 0 0 14 3l3-3m0 6v-6h-6',
  send: 'm3 3 19 9-19 9 4-9-4-9Zm4 9h15',
  plus: 'M12 4v16M4 12h16',
  close: 'm5 5 14 14M19 5 5 19',
  download: 'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
  play: 'm7 3 15 9-15 9V3Z',
  check: 'm4 12 5 5L20 6',
  book: 'M12 5c-4-3-7-3-10-2v16c3-1 6-1 10 2 4-3 7-3 10-2V3c-3-1-6-1-10 2Zm0 0v16',
} as const;
export type IconName = keyof typeof paths;

export function Icon({ name, className = '' }: { name: IconName; className?: string }) {
  return <svg className={`icon ${className}`} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={paths[name]} /></svg>;
}

export function ModeBadge({ mode, replay = false }: { mode: string; replay?: boolean }) {
  const isLive = mode === 'live';
  return <span className={`badge ${isLive ? 'badge-live' : 'badge-neutral'}`}>
    {replay ? 'REPLAY · ' : ''}{isLive ? 'LIVE' : 'ARCHIVIO'}
  </span>;
}

export function EventOrigin({ event }: { event: RunEvent }) {
  const details = protocolDetails(event);
  return <>
    <strong>Agente: {redactText(event.agent)}</strong>
    {details && <span className="badge badge-neutral">{redactText(details.protocol)}</span>}
    {details?.service && <span className="service-target">Servizio: {redactText(details.service)}</span>}
  </>;
}

export function ErrorBox({ message, retry, title = 'Operazione non riuscita' }: {
  message: string; retry?: () => void; title?: string;
}) {
  return <div className="notice notice-error" role="alert">
    <div><strong>{title}</strong><p>{message}</p></div>
    {retry && <button type="button" className="button button-small" onClick={retry}><Icon name="refresh" />Riprova</button>}
  </div>;
}

export function EmptyState({ title, children, icon = 'trace' }: {
  title: string; children: ReactNode; icon?: IconName;
}) {
  return <div className="empty-state"><span className="empty-icon"><Icon name={icon} /></span><h3>{title}</h3><p>{children}</p></div>;
}

export function JsonBlock({ value, label, initiallyOpen = true }: {
  value: unknown; label: string; initiallyOpen?: boolean;
}) {
  const id = useId();
  const [copyError, setCopyError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const text = value === undefined ? 'Dato non registrato dal backend.' : safeJson(value);
  const copy = async () => {
    setCopied(false);
    setCopyError(null);
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
    } catch (error) {
      setCopyError(`Copia non disponibile: ${errorMessage(error)} Puoi selezionare il testo qui sotto.`);
    }
  };
  return <details className="json-block" open={initiallyOpen}>
    <summary id={id}>{label}</summary>
    <div className="json-toolbar"><span>JSON registrato · credenziali oscurate</span><button type="button" className="button button-small" onClick={() => { void copy(); }}>{copied ? 'Copiato' : 'Copia JSON'}</button></div>
    {copyError && <p className="inline-error" role="alert">{copyError}</p>}
    <pre aria-labelledby={id} tabIndex={0}>{text}</pre>
  </details>;
}

export function SectionHeading({ eyebrow, title, children }: {
  eyebrow?: string; title: string; children?: ReactNode;
}) {
  return <div className="section-heading"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h2>{title}</h2></div>{children}</div>;
}

export class AppErrorBoundary extends Component<{ children: ReactNode }, { message: string | null }> {
  state: { message: string | null } = { message: null };
  static getDerivedStateFromError(error: unknown) {
    return { message: errorMessage(error) };
  }
  componentDidCatch(_error: Error, _info: ErrorInfo) {
    // Errors are rendered locally instead of forwarding prompts or traces to external telemetry.
  }
  render() {
    if (this.state.message) {
      return <main className="fatal-error"><h1>Impossibile visualizzare il laboratorio</h1><ErrorBox message={this.state.message} retry={() => window.location.reload()} /><p>Ricaricare legge soltanto lo stato: nessun turno viene inviato automaticamente.</p></main>;
    }
    return this.props.children;
  }
}
