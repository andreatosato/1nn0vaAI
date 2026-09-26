import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import type { Product, RunConfiguration } from '../contracts';
import type { DemoState } from '../hooks/useDemo';
import { dateTime, shortId } from '../lib/format';
import { redactText } from '../lib/redaction';
import { EmptyState, ErrorBox, Icon, ModeBadge } from './Common';
import { ConfigurationSummary } from './ConfigurationSummary';
import { ResultProducts } from './ProductCards';

export function ChatView({ demo, settings, onSettingsChange, draft, setDraft, active, guideRequest }: {
  demo: DemoState; settings: RunConfiguration | null; active: boolean;
  onSettingsChange: Dispatch<SetStateAction<RunConfiguration | null>>;
  draft: string; setDraft: Dispatch<SetStateAction<string>>;
  guideRequest?: { scenarioId: string; label: string } | null;
}) {
  const [liveAuthorized, setLiveAuthorized] = useState(false);
  const [scenarioId, setScenarioId] = useState('main-six-turns');
  const guide = useRef<HTMLDetailsElement>(null);
  const options = useRef<HTMLDetailsElement>(null);
  const messageArea = useRef<HTMLDivElement>(null);
  const following = useRef(true);
  const previousConversation = useRef<string | undefined>(undefined);
  const wasActive = useRef(false);
  const [unread, setUnread] = useState(false);
  const showLatest = () => {
    const area = messageArea.current;
    const last = area?.querySelector<HTMLElement>('.message:last-child');
    if (area) area.scrollTop = last ? Math.min(last.offsetTop, area.scrollHeight - area.clientHeight) : area.scrollHeight;
    following.current = true;
    setUnread(false);
  };
  useLayoutEffect(() => {
    const changedConversation = previousConversation.current !== demo.conversation?.id;
    if (active && (!wasActive.current || changedConversation)) following.current = true;
    previousConversation.current = demo.conversation?.id;
    wasActive.current = active;
    if (!active) return;
    if (following.current) showLatest();
    else setUnread(true);
  }, [active, demo.conversation?.id, demo.conversation?.messages, demo.pendingText, demo.monitor.answer, demo.monitor.record?.result?.answer]);
  useEffect(() => {
    if (!guideRequest) return;
    setScenarioId(guideRequest.scenarioId);
    setLiveAuthorized(false);
    if (options.current) options.current.open = true;
    if (guide.current) guide.current.open = true;
  }, [guideRequest]);
  useEffect(() => { setLiveAuthorized(false); }, [settings, demo.tracked?.runId]);
  const allProducts = demo.products.data ?? [];
  const developmentScenarios = demo.scenarios.data?.filter((item) => item.split === 'development') ?? [];
  const scenario = developmentScenarios.find((item) => item.id === scenarioId)
    ?? developmentScenarios.find((item) => item.id === 'main-six-turns')
    ?? developmentScenarios[0];
  const selectedStep = scenario?.turns.findIndex((turn) => turn.message === draft) ?? -1;
  const record = demo.monitor.record;
  const messages = demo.conversation?.messages ?? [];
  const hasRecordedAnswer = Boolean(record && messages.some((message) => message.runId === record.id && message.role === 'assistant'));
  const hasRecordedUser = Boolean(demo.tracked && messages.some((message) => message.runId === demo.tracked?.runId && message.role === 'user'));
  const chooseProduct = (product: Product) => {
    setDraft(`Vorrei informazioni sul prodotto #${product.id}: ${product.title}.`);
    setLiveAuthorized(false);
    if (settings) onSettingsChange({ ...settings, confirmAction: false });
    document.getElementById('chat-message')?.focus();
  };
  const submit = async (event: React.SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!settings) return;
    const submittedText = draft.trim();
    const accepted = await demo.send(draft, settings, liveAuthorized);
    if (accepted) {
      following.current = true;
      showLatest();
      setDraft((current) => current.trim() === submittedText ? '' : current);
      setLiveAuthorized(false);
      onSettingsChange((current) => current ? { ...current, confirmAction: false } : current);
    }
  };

  return <section className="chat-panel" aria-label="Conversazione con il Router">
      <div className="chat-tools">
      <details className="chat-options" ref={options}>
        <summary>Opzioni chat</summary>
        <div className="chat-options-content">
        <button type="button" className="button button-small" disabled={demo.busy || !demo.configuration.data || Boolean(demo.configuration.error)} onClick={() => { void demo.newConversation(); }}><Icon name="plus" />Nuova chat</button>
      <div className="conversation-picker">
        <label htmlFor="conversation-picker">Conversazione</label>
        <select id="conversation-picker" value={demo.conversation?.id ?? ''} disabled={demo.busy} onChange={(event) => {
          if (event.target.value) { void demo.selectConversation(event.target.value); }
        }}><option value="">{demo.conversation ? 'Seleziona una conversazione' : 'Nuova · verrà creata al primo invio'}</option>
          {demo.conversation && !demo.conversations.data?.some((item) => item.id === demo.conversation?.id) &&
            <option value={demo.conversation.id}>{demo.conversation.title}</option>}
          {demo.conversations.data?.map((item) => <option key={item.id} value={item.id}>{item.title} · {shortId(item.id)}</option>)}
        </select>
      </div>
      <details className="scenario-guide" ref={guide}>
        <summary><Icon name="book" />Percorso guidato · sei turni</summary>
        {guideRequest && <p>Ultimo preset preparato: <strong>{guideRequest.label}</strong>. Le impostazioni restano modificabili: controlla il riepilogo prima di inviare.</p>}
        <p>Sei turni oppure altre domande DEVELOPMENT da <code>/scenarios</code>. I casi holdout sono esclusi da questa guida didattica.</p>
        <p>Scegliere uno scenario non cambia bozza o storico. Un click su una domanda sostituisce il testo della bozza, <strong>non lo invia</strong>, e azzera i consensi. Ogni turno parte soltanto con “Invia”.</p>
        <label htmlFor="chat-scenario">Scenario DEVELOPMENT</label>
        <select id="chat-scenario" value={scenario?.id ?? ''} disabled={demo.busy || developmentScenarios.length === 0} onChange={(event) => setScenarioId(event.target.value)}>
          {developmentScenarios.length === 0 && <option value="">Nessuno scenario DEVELOPMENT disponibile</option>}
          {developmentScenarios.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.turns.length} {item.turns.length === 1 ? 'turno' : 'turni'}</option>)}
        </select>
        <p className="muted">Per i sei turni usa la stessa conversazione, in ordine, aspettando ogni risposta. Per le domande autonome usa “Nuova chat” se vuoi evitare il contesto precedente: cambiare scenario non crea una conversazione.</p>
        <p className="muted"><strong>Aspettative, non misure:</strong> dominio e fatti attesi provengono dallo scenario; confrontali con la risposta e con Traccia/Inspector dopo l'invio. In A2A il Router delega ad agenti specialisti; Inline e Skills hanno un solo agente Router e tre servizi business.</p>
        {active && demo.scenarios.loading && <p role="status">Caricamento scenari dal backend…</p>}
        {scenario ? <ol className="scenario-steps">{scenario.turns.map((turn, index) => <li key={`${scenario.id}-${index}`}>
          <button type="button" aria-pressed={selectedStep === index} disabled={demo.busy || Boolean(demo.failedSubmission)} onClick={() => {
            setDraft(turn.message); setLiveAuthorized(false);
            if (settings) onSettingsChange({ ...settings, confirmAction: false });
            if (options.current) options.current.open = false;
            document.getElementById('chat-message')?.focus({ preventScroll: true });
          }}><span className="step-number">{index + 1}</span><span>{turn.message}</span>{turn.confirmAction && <span className="step-confirm">Conferma richiesta</span>}</button>
          <p className="muted"><strong>Dominio atteso:</strong> {turn.expectedIntent} · Router coordina.
            {' '}<strong>Fatti/output attesi:</strong> {turn.expectedFacts.length > 0 ? turn.expectedFacts.join(' · ') : 'Nessun fatto testuale obbligatorio definito; verificare la risposta e le fonti.'}
          </p>
        </li>)}</ol> : !demo.scenarios.loading && !demo.scenarios.error && <p className="muted">Questa API non ha restituito scenari DEVELOPMENT.</p>}
        <p className="scenario-facts"><strong>Caso didattico sintetico al 2026-09-23:</strong> ORD-1042 · Blue &amp; Black Check Shirt, #83 · listino 29.99 USD, pagato 19.99 USD · consegna 2026-09-05, 18 giorni · outlet: ripensamento entro 14 giorni, difetto entro 60 giorni. Sono vincoli della fixture, non risultati del modello né policy commerciali reali.</p>
      </details>
      {settings && <ConfigurationSummary title="Impostazioni del prossimo messaggio" settings={settings} server={demo.configuration.data} />}
      <label className="checkbox-label"><input type="checkbox" checked={settings?.confirmAction ?? false} disabled={demo.busy || !settings || Boolean(demo.failedSubmission)} onChange={(event) => {
        if (settings) onSettingsChange({ ...settings, confirmAction: event.target.checked });
      }} /><span><strong>Autorizzo solo bozza sintetica</strong><small>Consenso valido per il prossimo turno, mai per rimborsi o azioni reali.</small></span></label>
      </div>
      </details>
      </div>
      {active && (demo.conversations.error || demo.scenarios.error) && <div className="chat-feedback">
        {demo.conversations.error && <ErrorBox message={demo.conversations.error} retry={demo.conversations.reload} title="Storico conversazioni non disponibile" />}
        {demo.scenarios.error && <ErrorBox message={demo.scenarios.error} retry={demo.scenarios.reload} title="Scenari non disponibili" />}
      </div>}
      <div ref={messageArea} className="messages" role="log" tabIndex={0} aria-label="Aggiornamenti conversazione" aria-live={active ? 'polite' : 'off'} aria-relevant="additions text" onScroll={(event) => {
        const area = event.currentTarget;
        following.current = area.scrollHeight - area.scrollTop - area.clientHeight < 48;
        if (following.current) setUnread(false);
      }}>
        {messages.length === 0 && !demo.pendingText && <EmptyState title={demo.configuration.data && !demo.configuration.error ? 'Il laboratorio è pronto' : 'In attesa del backend'} icon="chat">{demo.configuration.data && !demo.configuration.error ? 'Scegli un turno guidato o scrivi una richiesta.' : 'Carica una configurazione API valida per iniziare.'} Non sono stati avviati turni o modelli automaticamente.</EmptyState>}
        <ol className="message-list" aria-label="Messaggi registrati">
          {messages.map((message) => <li key={message.id} className={`message message-${message.role === 'user' ? 'user' : 'assistant'}`}>
            <div className="message-heading"><strong>{message.role === 'user' ? 'Tu' : message.role === 'assistant' ? 'Assistente' : message.role}</strong><time dateTime={message.at}>{dateTime(message.at)}</time>{message.runId && <span title={message.runId}>run {shortId(message.runId)}</span>}</div>
            <p className="message-text">{redactText(message.text)}</p>
            <ResultProducts ids={message.productIds.length > 0 ? message.productIds : record && message.role === 'assistant' && message.runId === record.id ? record.result?.productIds ?? [] : []} products={allProducts} onAsk={chooseProduct} />
            {message.sources.length > 0 && <ul className="source-list" aria-label="Fonti della risposta">{message.sources.map((source, index) => <li key={`${source}-${index}`}>{redactText(source)}</li>)}</ul>}
          </li>)}
          {demo.pendingText && !hasRecordedUser && <li className="message message-user"><div className="message-heading"><strong>Tu</strong><span>Invio accettato dal backend</span></div><p className="message-text">{redactText(demo.pendingText)}</p></li>}
          {!hasRecordedAnswer && (demo.monitor.answer || record?.result) && <li className="message message-assistant">
            <div className="message-heading"><strong>Assistente</strong><span>{record?.result ? 'Risultato del run' : 'Testo ricevuto via SSE'}</span><ModeBadge mode={record?.configuration.mode ?? settings?.mode ?? 'live'} /></div>
            <p className="message-text">{redactText(record?.result?.answer ?? demo.monitor.answer)}</p>
            <ResultProducts ids={record?.result?.productIds ?? []} products={allProducts} onAsk={chooseProduct} />
          </li>}
        </ol>
        {active && demo.running && <p className="running-note" role="status"><span className="status-dot" />In attesa della risposta del Router…</p>}
      </div>
      {unread && <button type="button" className="button button-small latest-message" onClick={showLatest}>Vai all'ultima risposta</button>}
      <form className="composer" onSubmit={(event) => { void submit(event); }}>
        {settings && <p className="chat-send-summary">{settings.modelProfileId} · {settings.unboundedExecution ? 'Senza limiti applicativi' : settings.approvedBudgetUsd != null ? `Budget ${settings.approvedBudgetUsd} USD` : 'Budget da impostare in Configurazione'}</p>}
        <label htmlFor="chat-message">Messaggio al Router</label>
        <textarea id="chat-message" rows={2} value={draft} maxLength={12000} disabled={demo.busy || Boolean(demo.failedSubmission)} placeholder="Scrivi una domanda al Router…" onChange={(event) => {
          setDraft(event.target.value); setLiveAuthorized(false);
          if (settings?.confirmAction) onSettingsChange({ ...settings, confirmAction: false });
        }} onKeyDown={(event) => {
          if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
            event.preventDefault(); event.currentTarget.form?.requestSubmit();
          }
        }} aria-describedby="message-policy" />
        <p id="message-policy" className="sr-only">Solo testo e riferimenti prodotto. Le immagini del catalogo non vengono mai allegate al modello. Ctrl/⌘ + Invio per inviare.</p>
        {selectedStep >= 0 && scenario?.turns[selectedStep]?.confirmAction && !settings?.confirmAction && <p className="consent-notice">Il turno scelto richiede una conferma in Opzioni chat: il consenso non viene mai selezionato automaticamente.</p>}
        <div className="composer-footer">
          {settings?.mode === 'live' && <label className="checkbox-label live-consent"><input type="checkbox" checked={liveAuthorized} disabled={demo.busy} onChange={(event) => setLiveAuthorized(event.target.checked)} /><span>Autorizzo questo invio LIVE e i possibili costi del modello.</span></label>}
          <button className="button button-primary" type="submit" title="Ctrl/⌘ + Invio" disabled={demo.busy || Boolean(demo.failedSubmission) || !settings || !demo.configuration.data?.allowLive || !draft.trim() || Boolean(demo.configuration.error) || !liveAuthorized}><Icon name="send" />{demo.actionBusy ? 'Invio…' : 'Invia'}</button>
        </div>
      </form>
    </section>;
}
