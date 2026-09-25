import { useEffect, useId, useRef } from 'react';
import type { ReactNode } from 'react';
import type { Technology } from '../contracts';
import { demoMetadata } from '../metadata';
import { Icon, ModeBadge } from './Common';

export function ChatWidget({ technology, open, onOpenChange, focusRequest, mode, status, children }: {
  technology: Technology; open: boolean; onOpenChange: (open: boolean) => void;
  focusRequest: number; mode: string; status: string; children: ReactNode;
}) {
  const id = useId();
  const root = useRef<HTMLDivElement>(null);
  const panel = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const launcher = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  const title = demoMetadata[technology].title;

  useEffect(() => {
    if (open) {
      const composer = panel.current?.querySelector<HTMLTextAreaElement>('textarea:not(:disabled)');
      (composer ?? heading.current)?.focus();
    } else if (restoreFocus.current) {
      launcher.current?.focus({ preventScroll: true });
      restoreFocus.current = false;
    }
  }, [open, focusRequest]);

  useEffect(() => {
    if (!open) return;
    const compactViewport = window.matchMedia?.('(max-width: 1100px), (max-height: 640px)');
    const leavePanel = (event: FocusEvent) => {
      if (compactViewport?.matches && event.target instanceof Node && !root.current?.contains(event.target)) {
        onOpenChange(false);
      }
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !event.isComposing
        && event.target instanceof Node && (root.current?.contains(event.target) || event.target === document.body)) {
        event.preventDefault();
        restoreFocus.current = true;
        onOpenChange(false);
      }
    };
    document.addEventListener('focusin', leavePanel);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('focusin', leavePanel);
      document.removeEventListener('keydown', escape);
    };
  }, [open, onOpenChange]);

  const close = () => {
    restoreFocus.current = true;
    onOpenChange(false);
  };

  return <div ref={root} className={`chat-widget accent-${demoMetadata[technology].accent}`} data-open={open}>
    <section ref={panel} id={`${id}-panel`} className="chat-widget-panel" role="dialog" aria-modal="false"
      aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`} hidden={!open} inert={!open} aria-hidden={!open}>
      <header className="chat-widget-heading">
        <div><p className="eyebrow">Bot della demo attiva</p><h2 ref={heading} id={`${id}-title`} tabIndex={-1}>Chat con il Router · {title}</h2></div>
        <button type="button" className="button chat-widget-close" aria-label={`Chiudi chat ${title}`} onClick={close}><Icon name="close" /></button>
      </header>
      <div className="chat-widget-body" tabIndex={0} aria-label={`Contenuto chat ${title}`}>
        <div className="chat-widget-context">
          <div><span className="small-label">Prossimo invio</span><ModeBadge mode={mode} /><a href={`#/${technology}/chat`} aria-label={`Apri configurazione della demo ${title}`} onClick={() => {
            onOpenChange(false);
            document.getElementById('page-heading')?.focus();
          }}>Configurazione</a></div>
          <p id={`${id}-description`}>Solo demo {title}. Chiudere conserva bozza e conversazione, senza annullare il run. Esc chiude; Tab permette di tornare alla pagina.</p>
        </div>
        {children}
      </div>
    </section>
    <button ref={launcher} type="button" className="chat-widget-launcher" aria-haspopup="dialog"
      aria-expanded={open} aria-controls={`${id}-panel`} aria-describedby={`${id}-status`}
      aria-label={`${open ? 'Riduci' : 'Apri'} chat ${title}`} onClick={() => { if (open) close(); else onOpenChange(true); }}>
      <span className="chat-widget-launcher-icon"><Icon name={open ? 'close' : 'chat'} /></span>
      <span><strong>Bot · {title}</strong><span id={`${id}-status`} className="chat-widget-status" aria-live={open ? 'off' : 'polite'}>{status}</span></span>
    </button>
  </div>;
}
