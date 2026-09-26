import { useEffect, useLayoutEffect, useRef } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import type { RunConfiguration } from '../contracts';
import type { DemoState } from '../hooks/useDemo';
import { redactText } from '../lib/redaction';
import { ErrorBox, Icon } from './Common';

export function ChatView({ demo, settings, draft, setDraft, active }: {
  demo: DemoState; settings: RunConfiguration | null; active: boolean;
  onSettingsChange?: Dispatch<SetStateAction<RunConfiguration | null>>;
  draft: string; setDraft: Dispatch<SetStateAction<string>>;
  guideRequest?: { scenarioId: string; label: string } | null;
}) {
  const messageArea = useRef<HTMLDivElement>(null);
  const previousConversation = useRef<string | undefined>(undefined);

  useLayoutEffect(() => {
    if (!active) return;
    const area = messageArea.current;
    if (area) area.scrollTop = previousConversation.current !== demo.conversation?.id ? 0 : area.scrollHeight;
    previousConversation.current = demo.conversation?.id;
  }, [active, demo.conversation?.id, demo.conversation?.messages, demo.monitor.answer, demo.pendingText]);

  useEffect(() => {
    if (demo.actionError) document.getElementById('chat-message')?.focus({ preventScroll: true });
  }, [demo.actionError]);

  const messages = demo.conversation?.messages ?? [];
  const record = demo.monitor.record;
  const hasRecordedAnswer = Boolean(record && messages.some((message) => message.runId === record.id && message.role === 'assistant'));
  const hasRecordedUser = Boolean(demo.tracked && messages.some((message) => message.runId === demo.tracked?.runId && message.role === 'user'));

  const submit = async (event: React.SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!settings || !draft.trim()) return;
    if (settings.mode === 'live' && !window.confirm('Vuoi inviare la domanda al modello LIVE? Potrebbero essere applicati costi.')) return;
    const submittedText = draft.trim();
    const accepted = await demo.send(submittedText, settings, true);
    if (accepted) setDraft((current) => current.trim() === submittedText ? '' : current);
  };

  return <section className="chat-panel" aria-label="Conversazione con il Router">
    <div ref={messageArea} className="messages" role="log" tabIndex={0} aria-label="Conversazione" aria-live={active ? 'polite' : 'off'}>
      <ol className="message-list" aria-label="Messaggi">
        {messages.map((message) => <li key={message.id} className={`message message-${message.role === 'user' ? 'user' : 'assistant'}`}>
          <strong>{message.role === 'user' ? 'Domanda utente' : 'Risposta bot'}</strong>
          <p className="message-text">{redactText(message.text)}</p>
        </li>)}
        {demo.pendingText && !hasRecordedUser && <li className="message message-user">
          <strong>Domanda utente</strong>
          <p className="message-text">{redactText(demo.pendingText)}</p>
        </li>}
        {!hasRecordedAnswer && (demo.monitor.answer || record?.result) && <li className="message message-assistant">
          <strong>Risposta bot</strong>
          <p className="message-text">{redactText(record?.result?.answer ?? demo.monitor.answer)}</p>
        </li>}
      </ol>
      {demo.running && <p className="running-note" role="status"><span className="status-dot" />Risposta in arrivo…</p>}
    </div>
    {demo.actionError && <ErrorBox title="Invio non riuscito" message={demo.actionError} retry={demo.clearError} />}
    <form className="composer" onSubmit={(event) => { void submit(event); }}>
      <label htmlFor="chat-message">Domanda utente</label>
      <textarea id="chat-message" rows={2} value={draft} maxLength={12000} disabled={demo.busy || Boolean(demo.failedSubmission)}
        onChange={(event) => setDraft(event.target.value)} />
      <div className="composer-footer">
        <button className="button button-primary" type="submit"
          disabled={demo.busy || Boolean(demo.failedSubmission) || !settings || !demo.configuration.data?.allowLive || !draft.trim() || Boolean(demo.configuration.error)}>
          <Icon name="send" />{demo.actionBusy ? 'Invio…' : 'Invia'}
        </button>
      </div>
    </form>
  </section>;
}
