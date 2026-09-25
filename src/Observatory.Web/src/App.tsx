import { useEffect, useState } from 'react';
import { services, technologies } from './contracts';
import type { RunConfiguration, Technology } from './contracts';
import { DataView } from './components/DataView';
import { ChatView } from './components/ChatView';
import { ChatWidget } from './components/ChatWidget';
import { ErrorBox, Icon, ModeBadge } from './components/Common';
import { ConfigurationPanel, reconcileSettings } from './components/ConfigurationPanel';
import { ComparisonView } from './components/ComparisonView';
import type { PreparedComparison } from './lib/comparisonPresets';
import { HistoryView } from './components/HistoryView';
import { InspectorView } from './components/InspectorView';
import { RunStatus } from './components/RunStatus';
import { TraceView } from './components/TraceView';
import { UsageView } from './components/UsageView';
import { MeasureComparisonView } from './components/MeasureComparisonView';
import { useDemo } from './hooks/useDemo';
import { dateTime, statusLabel } from './lib/format';
import { redactText } from './lib/redaction';
import { demoMetadata, pages, serviceMetadata } from './metadata';
import type { Page } from './metadata';

type Route = { kind: 'home' } | { kind: 'comparison' } | { kind: 'data'; technology?: Technology }
  | { kind: 'demo'; technology: Technology; page: Page } | { kind: 'not-found' };

export function parseRoute(hash: string): Route {
  const parts = hash.replace(/^#/, '').split('/').filter(Boolean);
  if (parts.length === 0) return { kind: 'home' };
  if (parts.length === 1 && parts[0] === 'confronto') return { kind: 'comparison' };
  if (parts.length === 1 && parts[0] === 'dati') return { kind: 'data' };
  const technology = technologies.find((value) => value === parts[0]);
  if (technology && parts.length === 2 && parts[1] === 'data') return { kind: 'data', technology };
  const page = parts[1] === undefined ? 'chat'
    : parts[1] === 'agents' ? 'trace'
    : pages.find((value) => value.id === parts[1])?.id;
  return technology && page && parts.length <= 2
    ? { kind: 'demo', technology, page }
    : { kind: 'not-found' };
}

function Header({ technology, comparison, dataPage }: { technology?: Technology; comparison?: boolean; dataPage?: boolean }) {
  return <header className="site-header">
    <a className="brand" href="#/" aria-label="AI Observatory, home"><img src="/observatory.svg" alt="" width="42" height="42" /><span>AI <strong>Observatory</strong><small>Laboratorio agenti · conferenza</small></span></a>
    <nav className="technology-nav" aria-label="Seleziona demo"><a href="#/" aria-current={!technology && !comparison && !dataPage ? 'page' : undefined}>Panoramica</a>{(['a2a', 'skills', 'inline'] as const).map((tech) => <a href={`#/${tech}`} key={tech} aria-current={technology === tech && !dataPage ? 'page' : undefined}>{demoMetadata[tech].title}</a>)}<a href="#/dati" aria-current={dataPage ? 'page' : undefined}>Dati della demo</a><a href="#/confronto" aria-current={comparison ? 'page' : undefined}>Confronto misure</a></nav>
    <span className="header-caption">Osserva. Confronta. Verifica.</span>
  </header>;
}

function Home() {
  return <main id="main-content" className="home-page" tabIndex={-1}>
    <section className="home-hero">
      <div className="hero-copy"><p className="eyebrow">AI Observatory / architetture a confronto</p><h1 id="page-heading" tabIndex={-1}>Non solo la risposta.<br /><span>Tutto il percorso.</span></h1><p className="hero-description">Un router, tre servizi. Inline e Skills chiamano le API business; A2A delega ai tre agenti remoti. Stesso dominio, confini diversi da osservare.</p><div className="hero-labels"><span>Modalità LIVE</span><span>Catalogo pubblico DummyJSON</span><span>Ordini sintetici</span></div><p className="hero-safety">Aprire una demo non avvia modelli. Le chiamate LIVE richiedono backend e deployment abilitati, budget e consenso per ogni invio.</p></div>
      <div className="hero-diagram" aria-label="Router e servizi Catalog, Orders, Returns"><div className="hero-orbit" aria-hidden="true" /><span className="diagram-caption">Un router. Tre servizi.</span><div className="hero-router"><Icon name="network" /><strong>Router</strong><span>Chiama API o delega via A2A</span></div><div className="hero-specialists">{services.map((service) => <div key={service}><span className="mini-dot" /><strong>{serviceMetadata[service].name}</strong></div>)}</div><span className="diagram-footnote">Ogni servizio: API business · agente A2A · skill</span><span className="diagram-footnote">Schema didattico · non una traccia di esecuzione</span></div>
    </section>
    <section className="demo-selection" aria-labelledby="demo-selection-title"><div className="section-heading"><div><p className="eyebrow">Scegli il punto di osservazione</p><h2 id="demo-selection-title">Tre demo. Nessuna scatola nera.</h2></div><span className="muted">Conversazioni e storico separati</span></div><div className="demo-cards">{(['a2a', 'skills', 'inline'] as const).map((technology) => {
      const meta = demoMetadata[technology];
      return <a className={`demo-card accent-${meta.accent}`} href={`#/${technology}`} key={technology}><div className="demo-card-top"><span className="demo-number">{meta.number}</span><Icon name={technology === 'a2a' ? 'network' : technology === 'skills' ? 'book' : 'code'} /></div><p className="eyebrow">{meta.subtitle}</p><h3>{meta.title}</h3><p>{meta.description}</p><div className="demo-card-footer"><span>Apri laboratorio</span><Icon name="arrow" /></div></a>;
    })}</div></section>
    <section className="home-bottom"><div><Icon name="code" /><h3>Le richieste, non le ipotesi</h3><p>Inspector con prompt e history registrati, distinto tra catture logical e wire.</p></div><div><Icon name="chart" /><h3>Confronti senza risultati inventati</h3><p>GPT-5 ↔ GPT-6, Astra/Sol ↔ Luna. Prima un dry run; poi misure e provenienza effettive.</p></div><div><Icon name="check" /><h3>Immagini solo nell’interfaccia</h3><p>I prodotti decorano le risposte tramite ID. Nessuna immagine viene allegata alle richieste chat.</p></div></section>
  </main>;
}

function DemoWorkspace({ technology, page, sharedDataRoute = false }: { technology: Technology; page: Page | 'data'; sharedDataRoute?: boolean }) {
  const demo = useDemo(technology);
  const [settings, setSettings] = useState<RunConfiguration | null>(null);
  const [draft, setDraft] = useState('');
  const [chatOpen, setChatOpen] = useState(false);
  const [composerFocus, setComposerFocus] = useState(0);
  const [guideRequest, setGuideRequest] = useState<Pick<PreparedComparison, 'scenarioId' | 'label'> | null>(null);
  const server = demo.configuration.data;
  const metadata = demoMetadata[technology];
  const currentRun = demo.monitor.record;
  const chatStatus = demo.failedSubmission ? 'Invio da verificare' : demo.running ? 'Turno in corso'
    : demo.actionBusy ? 'Operazione in corso' : demo.configuration.error ? 'Backend non disponibile'
    : demo.configuration.loading ? 'Collegamento al backend' : currentRun ? `Run ${statusLabel(currentRun.status).toLowerCase()}` : 'Parla con il Router';

  useEffect(() => {
    if (!server) return;
    setSettings((previous) => reconcileSettings(previous, server));
  }, [server]);
  useEffect(() => {
    if (demo.tracked || demo.conversation) setSettings((previous) => previous ? { ...previous, confirmAction: false } : previous);
  }, [demo.tracked?.runId, demo.conversation?.id]);
  useEffect(() => {
    if (demo.pendingText) {
      setDraft((previous) => previous.trim() === demo.pendingText ? '' : previous);
    }
  }, [demo.pendingText]);

  return <><main id="main-content" className={`workspace workspace-with-chat${chatOpen ? ' workspace-chat-open' : ''} accent-${metadata.accent}`} tabIndex={-1}>
    {sharedDataRoute
      ? <section className="demo-heading"><div><p className="eyebrow">Catalogo · ordini · policy</p><h1 id="page-heading" tabIndex={-1}>Dati demo<span className="heading-separator">/</span><span className="heading-secondary">Condivisi tra gli agenti</span></h1><p>Le azioni “Chiedi” preparano un messaggio per la chat {metadata.title}; il dataset mostrato è condiviso.</p></div></section>
      : <><section className="demo-heading"><div><p className="eyebrow">Demo {metadata.number} / {metadata.subtitle}</p><h1 id="page-heading" tabIndex={-1}>{metadata.title}<span className="heading-separator">/</span><span className="heading-secondary">Osservabilità in pratica</span></h1><p>{metadata.description}</p></div><div className="demo-heading-badges"><ModeBadge mode={settings?.mode ?? 'live'} /><span className="badge badge-neutral">{technology === 'a2a' ? 'Router + 3 agenti remoti' : 'Solo Router · 3 servizi HTTP'}</span></div></section>
        <div className="data-provenance"><Icon name="check" /><div><strong>Dati di esempio, sempre dichiarati.</strong><span>{server ? redactText(server.dataNotice) : 'Catalogo pubblico DummyJSON; ordini e policy sintetici. Immagini esclusivamente nella UI.'}</span>{server?.catalogRetrievedAt && <small>Catalogo acquisito: {dateTime(server.catalogRetrievedAt)}{server.catalogHash ? ` · hash ${server.catalogHash.slice(0, 16)}…` : ''}</small>}</div></div>
      </>}
    <nav className="page-tabs" aria-label="Pagine del laboratorio">{pages.map((item) => <a href={`#/${technology}/${item.id}`} key={item.id} aria-current={!sharedDataRoute && page === item.id ? 'page' : undefined}><Icon name={item.icon} />{item.label}</a>)}</nav>
    {!sharedDataRoute && demo.configuration.loading && <p className="loading-line" role="status">Lettura configurazione API… Nessuna chiamata al modello.</p>}
    {!sharedDataRoute && demo.configuration.error && <ErrorBox message={demo.configuration.error} retry={demo.configuration.reload} title="La demo non può inviare richieste" />}
    {!sharedDataRoute && server && !settings && !demo.configuration.loading && <ErrorBox message="Il backend non ha restituito modelli, prompt o strategie di history supportati. Nessun profilo fittizio viene aggiunto." retry={demo.configuration.reload} />}
    {server && settings && page === 'chat' && <ConfigurationPanel api={demo.api} server={server} settings={settings} onChange={setSettings} disabled={demo.busy || Boolean(demo.failedSubmission)} />}
    {server && settings && page === 'examples' && <ComparisonView server={server} scenarios={demo.scenarios}
      disabled={demo.busy || Boolean(demo.failedSubmission) || Boolean(demo.configuration.error) || demo.configuration.loading}
      needsNewChat={Boolean(demo.tracked || demo.pendingText || demo.conversation?.messages.length)}
      onOpenChat={() => { setChatOpen(true); setComposerFocus((previous) => previous + 1); }}
      onApply={(preset) => {
        setSettings(preset.settings);
        setDraft(preset.message);
        setGuideRequest({ scenarioId: preset.scenarioId, label: preset.label });
        setChatOpen(true);
        setComposerFocus((previous) => previous + 1);
      }} />}
    {!sharedDataRoute && !chatOpen && <RunStatus demo={demo} />}
    {page === 'data' && <DataView api={demo.api} products={demo.products} onAsk={(message) => {
      setDraft(message);
      setSettings((previous) => previous ? { ...previous, confirmAction: false } : previous);
      setChatOpen(true);
      setComposerFocus((previous) => previous + 1);
    }} />}
    {page === 'trace' && <TraceView run={currentRun} events={demo.monitor.events} calls={demo.monitor.calls} />}
    {page === 'inspector' && <InspectorView calls={demo.monitor.calls} captures={demo.captures} />}
    {page === 'usage' && <UsageView run={currentRun} calls={demo.monitor.calls} runs={demo.runs.data}
      conversationId={demo.conversation?.id ?? null} runsError={demo.runs.error} />}
    {page === 'history' && <HistoryView demo={demo} settings={settings} />}
  </main>
    <ChatWidget technology={technology} open={chatOpen} onOpenChange={setChatOpen} focusRequest={composerFocus}
      mode={settings?.mode ?? 'live'} status={chatStatus}>
      {chatOpen && <RunStatus demo={demo} compact />}
      <ChatView demo={demo} settings={settings} onSettingsChange={setSettings} draft={draft} setDraft={setDraft} active={chatOpen} guideRequest={guideRequest} />
    </ChatWidget>
  </>;
}

export default function App() {
  const [route, setRoute] = useState<Route>(() => parseRoute(window.location.hash));
  const [lastTechnology, setLastTechnology] = useState<Technology>(() => {
    const initialRoute = parseRoute(window.location.hash);
    if (initialRoute.kind === 'demo') return initialRoute.technology;
    return initialRoute.kind === 'data' ? initialRoute.technology ?? 'inline' : 'inline';
  });
  useEffect(() => {
    const change = () => {
      const nextRoute = parseRoute(window.location.hash);
      setRoute(nextRoute);
      if (nextRoute.kind === 'demo') setLastTechnology(nextRoute.technology);
      else if (nextRoute.kind === 'data' && nextRoute.technology) setLastTechnology(nextRoute.technology);
    };
    window.addEventListener('hashchange', change);
    return () => window.removeEventListener('hashchange', change);
  }, []);
  useEffect(() => {
    document.title = route.kind === 'demo'
      ? `${demoMetadata[route.technology].title} · ${pages.find((page) => page.id === route.page)?.label ?? ''} | AI Observatory`
      : route.kind === 'comparison' ? 'Confronto misure | AI Observatory'
      : route.kind === 'data' ? 'Dati demo | AI Observatory'
      : 'AI Observatory · Laboratorio agenti';
    document.getElementById('page-heading')?.focus();
  }, [route]);
  return <>
    <a className="skip-link" href="#main-content" onClick={(event) => {
      event.preventDefault(); document.getElementById('main-content')?.focus();
    }}>Vai al contenuto</a>
    <Header {...(route.kind === 'demo' ? { technology: route.technology } : route.kind === 'data' ? { technology: lastTechnology } : {})}
      comparison={route.kind === 'comparison'} dataPage={route.kind === 'data'} />
    {route.kind === 'home' && <Home />}
    {(route.kind === 'demo' || route.kind === 'data') && <DemoWorkspace
      key={route.kind === 'demo' ? route.technology : route.technology ?? lastTechnology}
      technology={route.kind === 'demo' ? route.technology : route.technology ?? lastTechnology}
      page={route.kind === 'demo' ? route.page : 'data'} sharedDataRoute={route.kind === 'data'} />}
    {route.kind === 'comparison' && <MeasureComparisonView />}
    {route.kind === 'not-found' && <main id="main-content" className="fatal-error" tabIndex={-1}><h1 id="page-heading" tabIndex={-1}>Pagina non trovata</h1><p>Questa route non corrisponde a una demo.</p><a className="button button-primary" href="#/">Torna alla panoramica</a></main>}
    <footer className="site-footer"><span>AI Observatory <span aria-hidden="true">/</span> Esperimenti trasparenti, non promesse sui modelli.</span><span>DummyJSON pubblico · ordini sintetici · credenziali fuori dal browser</span></footer>
  </>;
}
