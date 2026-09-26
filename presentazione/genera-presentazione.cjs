const pptxgen = require('pptxgenjs');
const path = require('path');
const fs = require('node:fs');
const assert = require('node:assert/strict');

const measurementsDirectory = path.resolve(__dirname, process.argv[2] || 'misurazioni-skill-remote-unbounded');
const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const measurements = readJson(path.join(measurementsDirectory, 'risultati.json'));
assert.equal(measurements.rows.length, 6);
assert.equal(measurements.experiment.configuration.unboundedExecution, true);
assert.equal(measurements.experiment.configuration.approvedBudgetUsd, null);
assert.equal(measurements.assertions.allActualMaxOutputTokensNull, true);
assert.equal(measurements.assertions.tokenAndCostTotalsMatch, true);
const measuredRows = ['inline', 'skills', 'a2a'].flatMap(architecture =>
  [1, 2].map(repetition => {
    const row = measurements.rows.find(r => r.architecture === architecture && r.repetition === repetition);
    assert.ok(row, `Missing measurement: ${architecture}/${repetition}`);
    return row;
  }));
const evidence = measuredRows.map(row => readJson(path.join(measurementsDirectory, row.exportFile)));
for (const [i, bundle] of evidence.entries()) {
  assert.equal(bundle.run.id, measuredRows[i].runId);
  assert.equal(bundle.run.message, measurements.experiment.question);
  assert.deepEqual(bundle.run.configuration, measurements.experiment.configuration);
  assert.equal(bundle.provenance.catalog.contentHash, measurements.catalogHash);
  assert.deepEqual(bundle.provenance.request.configuration, bundle.run.configuration);
  assert.equal(bundle.run.status, measuredRows[i].status);
  assert.equal(bundle.run.error, measuredRows[i].error);
  assert.equal(bundle.run.result?.answer ?? null, measuredRows[i].answer);
  assert.equal(bundle.run.durationMs, measuredRows[i].durationMs);
  assert.equal(bundle.ledger.calls.length, measuredRows[i].calls);
  assert.equal(bundle.run.inputTokens, measuredRows[i].input);
  assert.equal(bundle.run.outputTokens, measuredRows[i].output);
  assert.equal(bundle.run.estimatedCostUsd, measuredRows[i].costUsd);
  for (const [field, key] of Object.entries({input:'inputTokens',cached:'cachedInputTokens',cacheWrite:'cacheWriteTokens',output:'outputTokens',reasoning:'reasoningTokens'})) {
    const expected = bundle.ledger.calls.every(call => Number.isFinite(call[key]))
      ? bundle.ledger.calls.reduce((total, call) => total + call[key], 0) : null;
    assert.equal(expected, measuredRows[i][field]);
  }
  const agents = [...new Set(bundle.ledger.calls.map(call => call.agent))];
  assert.deepEqual(agents, measuredRows[i].agents);
  assert.deepEqual(Object.fromEntries(agents.map(agent => [agent,
    [...new Set(bundle.ledger.calls.filter(call => call.agent === agent).map(call => call.request.instructions.length))],
  ])), measuredRows[i].promptCharacters);
  const failures = bundle.timeline.filter(event => event.kind === 'tool.called' && event.data?.status === 'failed');
  assert.equal(failures.length, measuredRows[i].toolFailures);
  assert.deepEqual(failures.map(event => ({agent:event.agent,message:event.message,data:event.data})), measuredRows[i].errors);
  assert.deepEqual(bundle.timeline.filter(event => event.kind === 'tool.called' &&
    (event.message === 'assess_return' || event.data?.name === 'assess_return'))
    .map(event => ({agent:event.agent, ...event.data})), measuredRows[i].assessments);
  assert.ok(bundle.ledger.calls.every(call => call.mode === 'live' && call.usageSource === 'provider'));
  assert.ok(bundle.ledger.calls.every(call => call.request.parameters.maxOutputTokens === null));
  assert.ok(Math.abs(bundle.ledger.calls.reduce((total, call) => total + call.estimatedCostUsd, 0) - measuredRows[i].costUsd) < 1e-9);
}
assert.equal(new Set(evidence.map(bundle => bundle.run.conversationId)).size, 6);
assert.equal(new Set(measuredRows.map(row => row.runId)).size, 6);
assert.ok(Math.abs(measuredRows.reduce((total, row) => total + row.costUsd, 0) - measurements.totalControlledCostUsd) < 1e-9);
assert.equal(measuredRows.flatMap(row => row.assessments).length, measurements.quality.assessmentAttempts);
assert.equal(measuredRows.flatMap(row => row.assessments).filter(attempt => attempt.status === 'completed').length, measurements.quality.successfulAssessments);
assert.equal(measuredRows.flatMap(row => row.assessments).filter(attempt => attempt.status === 'failed' &&
  attempt.error === 'Motivo valido: defect, change-of-mind oppure unknown.').length, measurements.quality.validationErrors);
const formatNumber = (value, digits = 0) => value == null ? 'N/D' : value.toLocaleString('it-IT', {
  minimumFractionDigits: digits, maximumFractionDigits: digits,
});
const runReferences = measuredRows.map(r => `${r.architecture} ${r.repetition}: ${r.runId} (${r.status})`).join('\n');
const architectureLabel = architecture => ({inline:'Inline',skills:'Skills',a2a:'A2A'})[architecture];
const currentSummary = `${measuredRows.filter(r => r.status === 'completed').length}/6 completed · ${measuredRows.filter(r => r.answer).length}/6 risposte · ${measuredRows.reduce((total, r) => total + r.toolFailures, 0)} errori tool`;
const measurementDate = new Date(measurements.experiment.startedAt);
const measurementDay = measurementDate.toLocaleDateString('it-IT', { day: 'numeric', month: 'long', year: 'numeric' });

// Tools and agent descriptions exactly as recorded in the first model call of each architecture.
const firstRequest = architecture => evidence[measuredRows.findIndex(row => row.architecture === architecture)].ledger.calls[0].request;
const describeParameters = tool => Object.keys(tool.parameters?.properties ?? {})
  .map(name => (tool.parameters.required ?? []).includes(name) ? name : `${name}?`).join(', ');
const recordedTools = [...new Map(['inline', 'skills', 'a2a'].flatMap(architecture => firstRequest(architecture).tools)
  .map(tool => [tool.name, { name: tool.name, description: tool.description, parameters: describeParameters(tool) }])).values()];
const toolApis = { search_products: 'catalog', query_catalog: 'catalog', get_catalog_facets: 'catalog', get_product: 'catalog',
  get_order: 'orders', create_return_draft: 'orders', assess_return: 'returns', get_policies: 'returns' };
const domainTools = firstRequest('inline').tools.map(tool => ({
  name: tool.name, description: tool.description, parameters: describeParameters(tool), api: `shop-${toolApis[tool.name]}`,
}));
assert.deepEqual(domainTools.map(tool => tool.name).sort(), Object.keys(toolApis).sort());
const firstSentence = text => (text.match(/^.*?\.(?=\s|$)/)?.[0] ?? text);
const toolSchemaCharacters = Object.fromEntries(['inline', 'skills', 'a2a'].map(architecture =>
  [architecture, JSON.stringify(firstRequest(architecture).tools).length]));
const failedAssessments = measuredRows.flatMap(row => row.assessments).filter(attempt => attempt.status === 'failed');
const assessmentReason = failedAssessments[0]?.arguments?.reason ?? 'N/D';
const assessmentError = failedAssessments[0]?.error ?? 'N/D';
const specialists = ['catalog', 'orders', 'returns'].map(name => {
  const markdown = fs.readFileSync(path.join(__dirname, '..', 'src', 'Skills', `Observatory.Skill.${name[0].toUpperCase()}${name.slice(1)}`, 'skills', name, 'SKILL.md'), 'utf8').replace(/\r\n/g, '\n');
  const procedure = markdown.replace(/^---\n[\s\S]*?\n---\n/, '').replace(/^\s*#.*\n/, '').trim();
  const lines = procedure.split('\n');
  return {
    name,
    a2a: firstRequest('a2a').tools.find(tool => tool.name === `${name}_agent`).description,
    skill: markdown.match(/^description:\s*(.+)$/m)[1].trim(),
    procedure, procedureCharacters: procedure.length, procedureLines: lines.length, procedureFirstLine: lines[0].trim(),
  };
});
let totalMinutes = 0;

const pptx = new pptxgen();
pptx.layout = 'LAYOUT_WIDE';
pptx.author = 'Andrea Tosato';
pptx.subject = 'Osservabilità, qualità, latenza e costo dei sistemi AI';
pptx.title = "Osservare l'AI per migliorare la qualità delle risposte e abbassare il costo dei modelli";
pptx.company = '1nn0vaAI';
pptx.lang = 'it-IT';
pptx.theme = {
  headFontFace: 'Aptos Display',
  bodyFontFace: 'Aptos',
  lang: 'it-IT',
};
pptx.defineLayout({ name: 'WIDE', width: 13.333, height: 7.5 });
pptx.layout = 'WIDE';

const C = {
  navy: '102936',
  ink: '173342',
  muted: '526A76',
  teal: '076B62',
  mint: '7EE0CE',
  pale: 'E6F4F0',
  bg: 'F3F6F7',
  white: 'FFFFFF',
  line: 'D6E0E4',
  amber: 'BC6C07',
  amberPale: 'FFF4D6',
  red: 'A12A28',
  redPale: 'FFF1ED',
  blue: '2A6078',
  bluePale: 'EAF3F7',
};
const logo = path.join(__dirname, '..', 'src', 'Observatory.Web', 'public', 'observatory.svg');
const stocchiPhoto = path.join(__dirname, 'assets', 'tommaso-stocchi.png');
assert.ok(fs.existsSync(stocchiPhoto), 'Missing credited author photo: assets/tommaso-stocchi.png');
const sources = {
  reflection: 'https://chatgpt.com/s/t_6ab6cd6b438481919846351e496ee155',
  distributedSkills: 'https://devblogs.microsoft.com/agent-framework/from-specialist-agents-to-distributed-skills-over-mcp/',
  author: 'https://devblogs.microsoft.com/agent-framework/author/tstocchi/',
  photo: 'https://devblogs.microsoft.com/agent-framework/wp-content/uploads/sites/78/2024/09/codemotion25-small-150x150.webp',
  workflows: 'https://learn.microsoft.com/en-us/agent-framework/workflows/',
  orchestrations: 'https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/',
  handoff: 'https://learn.microsoft.com/en-us/agent-framework/workflows/orchestrations/handoff#differences-between-handoff-and-agent-as-tools',
};

function addHeader(slide, section, title, subtitle) {
  slide.background = { color: C.bg };
  slide.addText(section.toUpperCase(), { x: 0.65, y: 0.38, w: 8.2, h: 0.25, fontSize: 10, bold: true, color: C.teal, charSpacing: 2.2, margin: 0 });
  slide.addText(title, { x: 0.65, y: 0.76, w: 11.95, h: 0.64, fontSize: 27, bold: true, color: C.ink, margin: 0, breakLine: false, fit: 'shrink' });
  if (subtitle) slide.addText(subtitle, { x: 0.68, y: 1.43, w: 11.8, h: 0.4, fontSize: 13, color: C.muted, margin: 0, fit: 'shrink' });
  slide.addShape(pptx.ShapeType.line, { x: 0.65, y: 1.92, w: 12.0, h: 0, line: { color: C.line, width: 1 } });
}

// Slide number and time slot are computed when the slide's minutes are known (addNotes), so slides can be inserted freely.
let pendingFooter = null;
function footer(slide) { pendingFooter = slide; }
const clock = minutes => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
function renderFooter(slide, start, end) {
  slide.addText(`${String(pptx._slides.length).padStart(2, '0')}  ·  AI OBSERVATORY`, { x: 0.65, y: 7.08, w: 3, h: 0.2, fontSize: 8.5, bold: true, color: C.muted, charSpacing: 1, margin: 0 });
  slide.addText(`${clock(start)}–${clock(end)}`, { x: 10.5, y: 7.05, w: 2.15, h: 0.22, fontSize: 9, color: C.teal, bold: true, align: 'right', margin: 0 });
}

function card(slide, x, y, w, h, title, body, opts = {}) {
  slide.addShape(pptx.ShapeType.roundRect, { x, y, w, h, rectRadius: 0.08, fill: { color: opts.fill || C.white }, line: { color: opts.line || C.line, width: 1 }, shadow: opts.shadow === false ? undefined : { type: 'outer', color: '102936', opacity: 0.08, blur: 1, angle: 45, distance: 1 } });
  if (opts.kicker) slide.addText(opts.kicker, { x: x + 0.22, y: y + 0.18, w: w - 0.44, h: 0.22, fontSize: 9, bold: true, color: opts.accent || C.teal, charSpacing: 1.2, margin: 0 });
  slide.addText(title, { x: x + 0.22, y: y + (opts.kicker ? 0.5 : 0.24), w: w - 0.44, h: 0.38, fontSize: opts.titleSize || 16, bold: true, color: opts.titleColor || C.ink, margin: 0, fit: 'shrink' });
  if (body) slide.addText(body, { x: x + 0.22, y: y + (opts.kicker ? 0.98 : 0.75), w: w - 0.44, h: h - (opts.kicker ? 1.14 : 0.93), fontSize: opts.bodySize || 11.5, color: opts.bodyColor || C.muted, margin: 0.02, breakLine: false, valign: 'top', fit: 'shrink', bullet: opts.bullet ? { type: 'ul' } : undefined });
}

function pill(slide, x, y, w, text, color = C.teal, fill = C.pale) {
  slide.addShape(pptx.ShapeType.roundRect, { x, y, w, h: 0.36, rectRadius: 0.12, fill: { color: fill }, line: { color: fill } });
  slide.addText(text, { x: x + 0.08, y: y + 0.08, w: w - 0.16, h: 0.16, fontSize: 9, bold: true, color, align: 'center', margin: 0, fit: 'shrink' });
}

function addNotes(slide, minutes, notes) {
  const start = totalMinutes;
  totalMinutes += Number.parseInt(minutes, 10);
  if (pendingFooter === slide) { renderFooter(slide, start, totalMinutes); pendingFooter = null; }
  slide.addNotes(`[TEMPO: ${minutes}]\n${notes}`);
}

function addSource(slide, text, url) {
  slide.addText(text, { x: 0.68, y: 6.72, w: 11.9, h: 0.2, fontSize: 7.5, color: '718690', italic: true, margin: 0, fit: 'shrink', hyperlink: url ? { url } : undefined });
}

// 1
{
  const s = pptx.addSlide();
  s.background = { color: C.navy };
  s.addImage({ path: logo, x: 0.7, y: 0.65, w: 0.78, h: 0.78 });
  s.addText('AI OBSERVATORY', { x: 1.65, y: 0.74, w: 3.2, h: 0.25, fontSize: 11, bold: true, color: C.mint, charSpacing: 2.4, margin: 0 });
  s.addText("Osservare l'AI", { x: 0.72, y: 1.72, w: 11.8, h: 0.85, fontSize: 44, bold: true, color: C.white, margin: 0 });
  s.addText('per migliorare la qualità delle risposte\ne abbassare il costo dei modelli', { x: 0.75, y: 2.72, w: 10.8, h: 1.25, fontSize: 28, bold: true, color: C.mint, margin: 0, breakLine: false, fit: 'shrink' });
  s.addShape(pptx.ShapeType.line, { x: 0.75, y: 4.35, w: 5.1, h: 0, line: { color: '345461', width: 2 } });
  s.addText('Un caso reale di chatbot · telemetria · latenza · token · costo', { x: 0.75, y: 4.62, w: 8.8, h: 0.38, fontSize: 16, color: 'D6E6EA', margin: 0 });
  s.addText('Andrea Tosato', { x: 0.75, y: 6.27, w: 3.8, h: 0.32, fontSize: 17, bold: true, color: C.white, margin: 0 });
  s.addText('60 minuti · talk + demo LIVE', { x: 9.3, y: 6.31, w: 3.25, h: 0.26, fontSize: 12, color: C.mint, align: 'right', margin: 0 });
  addNotes(s, "1'", "Aprire con la domanda: una risposta fluida è davvero una buona risposta? Promessa: seguiremo una richiesta dall'utente al ledger. Dichiarare che scenario e dati sono sintetici, il catalogo è uno snapshot pubblico, mentre la telemetria LIVE è reale quando esplicitamente indicato.");
}

// 2
{
  const s = pptx.addSlide();
  addHeader(s, 'La domanda', 'La risposta è solo la punta dell’iceberg', 'Sotto il testo ci sono decisioni, tool, retry, token e denaro.');
  const xs = [0.75, 3.85, 6.95, 10.05];
  const items = [
    ['QUALITÀ', 'Fatti corretti?\nPolicy rispettate?\nFonti verificabili?', C.teal, C.pale],
    ['LATENZA', 'Quanto attendo?\nDove attendo?\nÈ variabilità o regressione?', C.blue, C.bluePale],
    ['TOKEN', 'Input utile?\nCache hit?\nOutput o reasoning?', C.amber, C.amberPale],
    ['COSTO', 'Per chiamata?\nPer run?\nPer esito corretto?', C.red, C.redPale],
  ];
  items.forEach((it, i) => {
    s.addShape(pptx.ShapeType.ellipse, { x: xs[i] + 0.78, y: 2.28, w: 1.08, h: 1.08, fill: { color: it[3] }, line: { color: it[2], width: 2 } });
    s.addText(String(i + 1), { x: xs[i] + 1.03, y: 2.57, w: 0.58, h: 0.36, fontSize: 22, bold: true, color: it[2], align: 'center', margin: 0 });
    s.addText(it[0], { x: xs[i], y: 3.62, w: 2.65, h: 0.28, fontSize: 12, bold: true, color: it[2], align: 'center', charSpacing: 1.2, margin: 0 });
    s.addText(it[1], { x: xs[i], y: 4.12, w: 2.65, h: 1.05, fontSize: 15, bold: true, color: C.ink, align: 'center', valign: 'mid', margin: 0, breakLine: false, fit: 'shrink' });
  });
  s.addText('Ottimizzare una sola metrica può peggiorare il sistema.', { x: 2.55, y: 5.73, w: 8.2, h: 0.46, fontSize: 22, bold: true, color: C.navy, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "2'", "Coinvolgere il pubblico: quale errore è più grave, tono poco elegante, prezzo inventato o reso autorizzato per il motivo sbagliato? La risposta introduce la necessità di misurare qualità e non solo velocità/costo.");
}

// Workflow overview belongs before the demo architecture.
{
  const s = pptx.addSlide();
  addHeader(s, 'La mappa dei workflow', 'Cinque orchestrazioni + agent-as-tool', 'Prima scegli chi decide e chi risponde. Poi scegli come collegare i componenti.');
  const patterns = [
    ['Sequential', 'A → B → C\nPipeline ordinata: ogni fase passa il risultato alla successiva.'],
    ['Concurrent', 'A ∥ B ∥ C → sintesi\nLavori indipendenti in parallelo; raccolta dei risultati.'],
    ['Handoff', 'A → B · cambia il responsabile\nIl destinatario prende il controllo del task e del dialogo.'],
    ['Group Chat', 'Manager ↔ conversazione condivisa\nTurni selezionati e raffinamento tra più agenti.'],
    ['Magentic', 'Pianifica → esegui → verifica → ripianifica\nCoordinamento adattivo per problemi aperti.'],
    ['Agent-as-tool', 'Principale → specialista → principale\nDelega circoscritta; la risposta finale resta al principale.'],
  ];
  patterns.forEach(([title, body], i) => card(s, 0.7 + (i % 3) * 4.18, 2.12 + Math.floor(i / 3) * 1.72, 3.74, 1.56,
    title, body, { fill: i === 5 ? C.pale : C.white, line: i === 5 ? C.teal : C.line, titleSize: 16, bodySize: 11, shadow: false }));
  const capabilities = [
    ['COMPOSIZIONE', 'Agenti nei workflow · workflow come agenti\nworkflow dichiarativi'],
    ['INTERAZIONE E DURABILITÀ', 'Human-in-the-loop\ncheckpoint e ripresa'],
    ['OPERATIVITÀ', 'Osservabilità\nvisualizzazione'],
  ];
  capabilities.forEach(([title, body], i) => {
    const x = 0.7 + i * 4.18;
    s.addText(title, { x, y: 5.65, w: 3.74, h: 0.19, fontSize: 9, bold: true, color: C.teal, charSpacing: 0.6, margin: 0 });
    s.addText(body, { x, y: 5.94, w: 3.74, h: 0.45, fontSize: 10.5, color: C.ink, margin: 0, fit: 'shrink' });
  });
  addSource(s, 'Fonte: Microsoft Learn · Workflow capabilities. Agent-as-tool è un pattern aggiuntivo, non il sesto workflow built-in.', sources.workflows);
  footer(s);
  addNotes(s, "2'", `Mappa iniziale completa delle voci della pagina Workflow capabilities, consultata il 25/09/2026: ${sources.workflows}
Le quattro categorie sono Composition (Agents in workflows, Workflows as agents, Declarative workflows), Interaction and durability (Human-in-the-loop, Checkpoints and resuming), Operations (Observability, Visualization) e Multi-agent orchestration (Sequential, Concurrent, Handoff, Group Chat, Magentic).
La slide distingue le capacità trasversali dai cinque pattern di orchestrazione: ${sources.orchestrations}
Sequential: percorso ordinato con passaggio dei risultati. Concurrent: fan-out su attività indipendenti e aggregazione, non necessariamente dialogo reciproco. Handoff: trasferimento del controllo al destinatario. Group Chat: un manager sceglie i turni in una conversazione condivisa. Magentic: pianificazione e controllo adattivo dei progressi; non promettere efficacia universale.
Agent-as-tool è aggiunto intenzionalmente come pattern di delega, NON come sesto builder nell'elenco Learn. Il principale invoca un altro agente, riceve il risultato e prosegue; non trasferisce l'intera ownership del task. Fonte del confronto: ${sources.handoff}
Executors, edges, events e state sono primitive del grafo; routing condizionale, fan-out/fan-in, iterazioni e composizione permettono workflow personalizzati. Non confonderli con ulteriori voci built-in: https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/
Questi pattern sono componibili. Nella demo A2A è il protocollo usato per una delega agent-as-tool, non un sesto tipo di workflow.`);
}

// 3
{
  const s = pptx.addSlide();
  addHeader(s, 'Metodo', 'Dalla sensazione a una rubrica verificabile', 'La telemetria spiega il percorso; la rubrica valuta l’esito.');
  const criteria = [
    ['Fatti', 'data consegna, stato, prezzo pagato'],
    ['Policy', '14 giorni vs difetto entro 60'],
    ['Contesto', 'correzione del motivo del reso'],
    ['Grounding', 'prodotti reali, ID e prezzi'],
    ['Consenso', 'nessuna azione senza conferma'],
  ];
  criteria.forEach((c, i) => {
    const y = 2.2 + i * 0.78;
    s.addShape(pptx.ShapeType.ellipse, { x: 0.83, y: y + 0.02, w: 0.36, h: 0.36, fill: { color: C.teal }, line: { color: C.teal } });
    s.addText('✓', { x: 0.91, y: y + 0.08, w: 0.2, h: 0.16, fontSize: 11, bold: true, color: C.white, align: 'center', margin: 0 });
    s.addText(c[0], { x: 1.42, y, w: 1.25, h: 0.34, fontSize: 15, bold: true, color: C.ink, margin: 0 });
    s.addText(c[1], { x: 2.78, y, w: 3.45, h: 0.34, fontSize: 13, color: C.muted, margin: 0, fit: 'shrink' });
  });
  card(s, 7.05, 2.15, 5.15, 3.8, 'Il caso ORD-1042', 'Listino: 29,99 USD\nPagato: 19,99 USD\nConsegna: 5 settembre 2026\nArticolo outlet\n\nIl cliente prima parla di ripensamento, poi chiarisce che il prodotto era difettoso.', { kicker: 'SCENARIO DI CORREZIONE', fill: C.white, accent: C.teal, bodySize: 14 });
  pill(s, 7.35, 5.25, 2.1, 'QUALITÀ ≠ STILE');
  pill(s, 9.65, 5.25, 2.2, 'TOKEN ≠ QUALITÀ', C.amber, C.amberPale);
  footer(s);
  addNotes(s, "2'", "Spiegare gli expected facts prima di guardare l'output. Una risposta può essere persuasiva e sbagliata. La rubrica permette di ripetere il confronto e separa giudizio qualitativo da telemetria quantitativa.");
}

// 4
{
  const s = pptx.addSlide();
  addHeader(s, 'Osservabilità', 'Seguiamo ogni richiesta end-to-end', 'Inline, Skills e A2A condividono dominio e dati, ma non lo stesso percorso.');
  const nodes = [
    [0.7, 3.05, 1.65, 'UTENTE', 'domanda'],
    [2.8, 3.05, 1.7, 'ROUTER', 'prompt + history'],
    [5.0, 2.25, 2.05, 'MODELLO', 'inferenza'],
    [5.0, 3.85, 2.05, 'TOOL / A2A', 'delega'],
    [7.65, 3.05, 2.0, 'SERVIZI', 'Catalog · Orders · Returns'],
    [10.25, 3.05, 2.25, 'TELEMETRIA', 'trace · usage · ledger'],
  ];
  nodes.forEach((n, i) => {
    s.addShape(pptx.ShapeType.roundRect, { x: n[0], y: n[1], w: n[2], h: 1.05, rectRadius: 0.06, fill: { color: i === 5 ? C.navy : C.white }, line: { color: i === 5 ? C.navy : C.line, width: 1.2 } });
    s.addText(n[3], { x: n[0] + 0.12, y: n[1] + 0.21, w: n[2] - 0.24, h: 0.22, fontSize: 12, bold: true, color: i === 5 ? C.mint : C.teal, align: 'center', margin: 0 });
    s.addText(n[4], { x: n[0] + 0.1, y: n[1] + 0.57, w: n[2] - 0.2, h: 0.18, fontSize: 9.5, color: i === 5 ? C.white : C.muted, align: 'center', margin: 0, fit: 'shrink' });
  });
  [[2.35,3.47,0.42,0],[4.5,3.47,0.42,-0.65],[4.5,3.47,0.42,0.82],[7.08,3.47,0.5,0],[9.68,3.47,0.5,0]].forEach(a => s.addShape(pptx.ShapeType.chevron, { x: a[0], y: a[1] + a[3], w: a[2], h: 0.24, fill: { color: C.mint }, line: { color: C.mint } }));
  s.addText('Una delega, un retry o una history diversa cambiano il numero di inferenze.', { x: 2.35, y: 5.55, w: 8.7, h: 0.4, fontSize: 18, bold: true, color: C.ink, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "2'", "Mostrare i tre percorsi: Inline e Skills hanno un solo agente modello, il Router; A2A delega agli specialisti remoti. I servizi business sono distinti dagli agenti. Il confronto tra architetture non isola automaticamente il solo overhead di rete. Le prossime tre slide mostrano come è fatto davvero il sistema.");
}

// 4b · Architettura (native shapes: editable in PowerPoint)
{
  const s = pptx.addSlide();
  addHeader(s, 'Architettura della demo', 'Un agente, un processo: 13 risorse orchestrate da Aspire', 'Stesso dominio e stessi tool; cambia dove vivono istruzioni e modello.');
  const box = (x, y, w, h, title, sub, fill, lineColor, titleColor = C.ink, subColor = C.muted, titleSize = 11) => {
    s.addShape(pptx.ShapeType.roundRect, { x, y, w, h, rectRadius: 0.05, fill: { color: fill }, line: { color: lineColor, width: 1.2 } });
    s.addText(title, { x: x + 0.05, y: y + 0.14, w: w - 0.1, h: 0.24, fontSize: titleSize, bold: true, color: titleColor, align: 'center', margin: 0, fit: 'shrink' });
    if (sub) s.addText(sub, { x: x + 0.06, y: y + 0.44, w: w - 0.12, h: h - 0.52, fontSize: 8.5, color: subColor, align: 'center', valign: 'top', margin: 0, fit: 'shrink' });
  };
  const arrow = (x1, y1, x2, y2, color = C.teal, dash = 'solid') => s.addShape(pptx.ShapeType.line, {
    x: Math.min(x1, x2), y: Math.min(y1, y2), w: Math.abs(x2 - x1) || 0.001, h: Math.abs(y2 - y1) || 0.001,
    flipV: y2 < y1, line: { color, width: 1.5, dashType: dash, endArrowType: 'triangle' },
  });
  const label = (x, y, w, text, color = C.muted) => s.addText(text, { x, y, w, h: 0.18, fontSize: 7.5, bold: true, color, align: 'center', margin: 0, fit: 'shrink' });
  const rows = [
    { y: 2.2, key: 'inline', router: 'router-inline', color: C.blue, fill: C.bluePale, path: 'INLINE · tutto in un processo', link: 'libreria', toShop: 'HTTP (router)' },
    { y: 3.62, key: 'skills', router: 'router-skills', color: C.teal, fill: C.pale, path: 'SKILLS · istruzioni remote, tool locali', link: 'GET SKILL.md', toShop: 'HTTP (router)' },
    { y: 5.04, key: 'a2a', router: 'router-a2a', color: C.amber, fill: C.amberPale, path: 'A2A · agenti remoti', link: 'A2A', toShop: 'HTTP (agente)' },
  ];
  const h = 1.12;
  box(0.55, 3.62, 1.3, h, 'web', 'React · Vite\nchat + telemetria', C.white, C.line);
  rows.forEach(row => {
    arrow(1.85, 3.62 + h / 2, 2.3, row.y + h / 2, C.muted);
    box(2.3, row.y, 2.2, h, row.router, 'MODELLO · istruzioni router\nledger token/costo', C.navy, C.navy, C.mint, C.white);
    arrow(4.5, row.y + h / 2, 5.3, row.y + h / 2, row.color, row.key === 'inline' ? 'dash' : 'solid');
    label(4.5, row.y + h / 2 - 0.24, 0.8, row.link, row.color);
    s.addText(row.path, { x: 5.3, y: row.y - 0.2, w: 3.65, h: 0.17, fontSize: 7.5, bold: true, color: row.color, charSpacing: 0.6, margin: 0, fit: 'shrink' });
    if (row.key === 'inline') box(5.3, row.y, 3.65, h, 'Observatory.Inline', 'libreria in-process (nessun processo in più)\nistruzioni dei 3 domini concatenate nel prompt', row.fill, row.color);
    else ['catalog', 'orders', 'returns'].forEach((domain, i) => box(5.3 + i * 1.24, row.y, 1.17, h,
      `${row.key === 'skills' ? 'skill' : 'agent'}-${domain}`,
      row.key === 'skills' ? 'NESSUN MODELLO\n/skills · SKILL.md' : 'MODELLO\nprocedura + tool', row.fill, row.color, C.ink, C.muted, 9.5));
    arrow(8.95, row.y + h / 2, 9.75, row.y + h / 2, row.color, row.key === 'skills' ? 'dash' : 'solid');
    label(8.95, row.y + h / 2 + 0.07, 0.8, row.toShop, row.color);
  });
  ['catalog', 'orders', 'returns'].forEach((domain, i) => box(9.75, rows[i].y, 2.9, h, `shop-${domain}`,
    'API di business · NESSUN MODELLO\nsystem of record sintetico', C.white, C.line));
  s.addShape(pptx.ShapeType.roundRect, { x: 0.55, y: 6.32, w: 12.1, h: 0.42, rectRadius: 0.05, fill: { color: C.navy }, line: { color: C.navy } });
  s.addText('Aspire AppHost · service discovery · ServiceDefaults = OpenTelemetry (trace, metriche, log) → Dashboard · il ledger aggiunge token, cache e costo per chiamata', {
    x: 0.75, y: 6.43, w: 11.7, h: 0.2, fontSize: 10, bold: true, color: C.mint, align: 'center', margin: 0, fit: 'shrink',
  });
  footer(s);
  addNotes(s, "2'", `Leggere la slide per righe. Inline: il router ha tutte le istruzioni dei tre domini nel prompt e tutti i tool, grazie alla libreria Observatory.Inline: nessun processo aggiuntivo.
Skills: il router scarica dagli skill site solo l'indice (GET /skills); il modello chiama load_skill e allora il router scarica SKILL.md (e read_skill_resource per le reference). Gli skill site non hanno modello: pubblicano istruzioni. I tool restano nel router, che chiama direttamente le API shop. Se uno skill site non risponde, la run si ferma con skill_unavailable (fail closed).
A2A: il router vede tre tool catalog_agent/orders_agent/returns_agent; ogni chiamata è una richiesta A2A a un agente con modello, istruzioni e tool propri, che a sua volta chiama la sua API shop.
Tutti i progetti usano ServiceDefaults: l'osservabilità è quella di Aspire (OpenTelemetry), arricchita dal ledger applicativo con token, cache e costo.`);
}

// 4c · Tool: la descrizione è parte del prompt
{
  const s = pptx.addSlide();
  addHeader(s, 'Tool', 'Otto tool di dominio: la descrizione è prompt', 'Nomi, parametri e descrizioni esatti inviati al modello nella prima chiamata Inline. Testi integrali nelle note.');
  const headers = ['TOOL', 'PARAMETRI', 'COSA DICE AL MODELLO (prima frase)', 'API'];
  const w = [1.75, 2.05, 3.85, 1.05];
  const x = w.map((_, i) => 0.65 + w.slice(0, i).reduce((a, b) => a + b, 0));
  headers.forEach((text, i) => {
    s.addShape(pptx.ShapeType.rect, { x: x[i], y: 2.1, w: w[i], h: 0.36, fill: { color: C.navy }, line: { color: C.white, width: 1 } });
    s.addText(text, { x: x[i] + 0.05, y: 2.2, w: w[i] - 0.1, h: 0.16, fontSize: 8.5, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
  });
  domainTools.forEach((tool, ri) => {
    const y = 2.46 + ri * 0.47;
    const failing = tool.name === 'assess_return';
    [tool.name, tool.parameters, firstSentence(tool.description), tool.api].forEach((value, i) => {
      s.addShape(pptx.ShapeType.rect, { x: x[i], y, w: w[i], h: 0.47, fill: { color: failing ? C.redPale : ri % 2 ? 'F7FAFA' : C.white }, line: { color: C.line, width: 0.8 } });
      s.addText(value, { x: x[i] + 0.06, y: y + 0.04, w: w[i] - 0.12, h: 0.39, fontSize: i === 2 ? 8.5 : 9, bold: i === 0, color: failing && i < 2 ? C.red : C.ink, fontFace: i < 2 ? 'Consolas' : undefined, valign: 'mid', align: i === 3 ? 'center' : 'left', margin: 0, fit: 'shrink' });
    });
  });
  card(s, 9.6, 2.1, 3.05, 1.72, 'Chi riceve i tool', 'Inline e Skills: il router riceve tutti gli 8.\nA2A: ogni agente solo i propri; il router vede 3 tool agente.\nSkills aggiunge load_skill e read_skill_resource.', { fill: C.white, titleSize: 12.5, bodySize: 9.5, shadow: false });
  card(s, 9.6, 3.98, 3.05, 2.28, `${measurements.quality.validationErrors}/${measurements.quality.assessmentAttempts} assess_return falliti`,
    `reason è una stringa libera: nessun valore ammesso nello schema.\nIl modello invia "${assessmentReason}".\nL'API risponde: "${assessmentError}"\n→ elencare i valori (enum) nella descrizione.`,
    { kicker: 'LEZIONE DALLE MISURE', fill: C.redPale, line: C.red, accent: C.red, titleSize: 12.5, bodySize: 9.5, shadow: false });
  addSource(s, `Fonte: request.tools della run ${measuredRows[0].runId}. Ogni schema tool è input fatturato a ogni chiamata: ${formatNumber(toolSchemaCharacters.inline)} car. Inline · ${formatNumber(toolSchemaCharacters.skills)} Skills · ${formatNumber(toolSchemaCharacters.a2a)} router A2A.`);
  footer(s);
  addNotes(s, "1'", `Il contratto del tool è un prompt: nome, descrizione e schema dei parametri vanno al modello a ogni chiamata e si pagano come input (sono nel prefisso, quindi cacheabili se stabili).
Nelle sei run: ${measurements.quality.caveat}
Il problema non è di rete: lo schema di assess_return non dichiara i valori ammessi di reason; il modello scrive il motivo in italiano. Fix suggerito (non applicato per non alterare le misure): enum o elenco dei valori nella descrizione.
DESCRIZIONI INTEGRALI REGISTRATE:
${recordedTools.map(tool => `${tool.name}(${tool.parameters}): ${tool.description}`).join('\n')}`);
}

// 4d · Descrizioni agentiche
{
  const s = pptx.addSlide();
  addHeader(s, 'Descrizioni agentiche', 'Lo stesso specialista, confezionato in tre modi', 'Cosa legge il modello per decidere a chi chiedere, e quando legge la procedura completa.');
  const rowLabels = [
    ['A2A · descrizione del tool agente', 'il router A2A la vede sempre'],
    ['SKILLS · description di SKILL.md', 'il router Skills la vede sempre'],
    ['PROCEDURA dello specialista', 'Inline: sempre nel prompt\nSkills: dopo load_skill\nA2A: nel prompt dell’agente'],
  ];
  rowLabels.forEach((row, i) => {
    const y = 2.55 + i * 1.3;
    s.addText(row[0], { x: 0.65, y, w: 2.3, h: 0.3, fontSize: 10, bold: true, color: [C.amber, C.teal, C.navy][i], margin: 0, fit: 'shrink' });
    s.addText(row[1], { x: 0.65, y: y + 0.32, w: 2.3, h: 0.75, fontSize: 8.5, color: C.muted, margin: 0, valign: 'top', fit: 'shrink' });
  });
  specialists.forEach((specialist, i) => {
    const x = 3.1 + i * 3.2;
    s.addText(specialist.name.toUpperCase(), { x, y: 2.08, w: 3.0, h: 0.3, fontSize: 13, bold: true, color: C.ink, align: 'center', margin: 0 });
    [specialist.a2a, specialist.skill, `${formatNumber(specialist.procedureCharacters)} caratteri · ${specialist.procedureLines} righe\n“${specialist.procedureFirstLine}”`].forEach((text, row) => {
      const y = 2.48 + row * 1.3;
      s.addShape(pptx.ShapeType.roundRect, { x, y, w: 3.0, h: 1.18, rectRadius: 0.05, fill: { color: [C.amberPale, C.pale, C.white][row] }, line: { color: [C.amber, C.teal, C.line][row], width: 1 } });
      s.addText(text, { x: x + 0.12, y: y + 0.08, w: 2.76, h: 1.02, fontSize: 9, color: C.ink, valign: 'mid', margin: 0, fit: 'shrink' });
    });
  });
  s.addText('Descrizione breve = routing economico. Procedura completa = solo quando serve (Skills) o solo dove serve (A2A).', {
    x: 0.65, y: 6.4, w: 12.0, h: 0.3, fontSize: 12.5, bold: true, color: C.ink, align: 'center', margin: 0, fit: 'shrink',
  });
  footer(s);
  addNotes(s, "1'", `Il modello sceglie lo specialista leggendo testo: la descrizione del tool agente (A2A) o la description della skill (Skills). Descrizioni vaghe o sovrapposte producono deleghe sbagliate o doppie.
La procedura completa (identica nei tre percorsi, verificata da un test) entra nel contesto in momenti diversi: sempre (Inline), su richiesta tramite load_skill (Skills), solo nel prompt dello specialista (A2A).
TESTI INTEGRALI:
${specialists.map(specialist => `== ${specialist.name}\nA2A: ${specialist.a2a}\nSKILL: ${specialist.skill}\nPROCEDURA:\n${specialist.procedure}`).join('\n\n')}`);
}

// Diagrams reconstructed from the shared reasoning as editable PowerPoint shapes.
{
  const s = pptx.addSlide();
  addHeader(s, 'A2A vs Skill · 1/3', 'Dove avviene il reasoning?', 'Stesso bisogno di dominio; cambia il numero di reasoner coinvolti.');
  const panel = (x, title, subtitle, color, fill) => {
    s.addShape(pptx.ShapeType.roundRect, { x, y: 2.12, w: 5.85, h: 4.28, rectRadius: 0.06, fill: { color: C.white }, line: { color, width: 1.3 } });
    s.addText(title, { x: x + 0.25, y: 2.34, w: 5.35, h: 0.3, fontSize: 17, bold: true, color, align: 'center', margin: 0 });
    s.addText(subtitle, { x: x + 0.35, y: 2.72, w: 5.15, h: 0.24, fontSize: 10.5, color: C.muted, align: 'center', margin: 0, fit: 'shrink' });
    s.addShape(pptx.ShapeType.roundRect, { x: x + 1.45, y: 3.14, w: 2.95, h: 0.72, rectRadius: 0.04, fill: { color: fill }, line: { color, width: 1 } });
    s.addText('MAIN AGENT', { x: x + 1.65, y: 3.36, w: 2.55, h: 0.22, fontSize: 12.5, bold: true, color, align: 'center', margin: 0 });
  };
  panel(0.7, 'A2A · Agent-as-tool', 'Il principale delega un task a un altro reasoner.', C.amber, C.amberPale);
  panel(6.78, 'Distributed Skill', 'Il principale acquisisce competenza e operazioni.', C.teal, C.pale);

  s.addShape(pptx.ShapeType.chevron, { x: 3.25, y: 3.95, w: 0.38, h: 0.32, rotate: 90, fill: { color: C.amber }, line: { color: C.amber } });
  s.addText('“chiama Weather”', { x: 1.72, y: 4.02, w: 2.25, h: 0.2, fontSize: 9.5, color: C.muted, align: 'center', margin: 0 });
  s.addShape(pptx.ShapeType.roundRect, { x: 2.15, y: 4.43, w: 2.95, h: 1.38, rectRadius: 0.04, fill: { color: C.amberPale }, line: { color: C.amber, width: 1 } });
  s.addText('WEATHER AGENT', { x: 2.35, y: 4.62, w: 2.55, h: 0.22, fontSize: 12.5, bold: true, color: C.amber, align: 'center', margin: 0 });
  s.addText('LLM → weather API → LLM', { x: 2.37, y: 5.05, w: 2.51, h: 0.25, fontSize: 10, color: C.ink, align: 'center', margin: 0, fit: 'shrink' });
  s.addText('risposta testuale → Main Agent', { x: 1.45, y: 5.84, w: 4.35, h: 0.22, fontSize: 10.5, bold: true, color: C.amber, align: 'center', margin: 0 });

  const skillSteps = [
    ['WEATHER SKILL', 'SKILL.md'],
    ['MCP TOOLS', 'weather_current() · weather_forecast()'],
    ['WEATHER SERVICE', 'API / dati di dominio'],
  ];
  skillSteps.forEach(([title, body], i) => {
    const y = 4.15 + i * 0.66;
    s.addShape(pptx.ShapeType.roundRect, { x: 8.15, y, w: 3.08, h: 0.52, rectRadius: 0.03, fill: { color: i === 2 ? C.bluePale : C.pale }, line: { color: i === 2 ? C.blue : C.teal, width: 1 } });
    s.addText(title, { x: 8.3, y: y + 0.08, w: 1.18, h: 0.18, fontSize: 9.5, bold: true, color: i === 2 ? C.blue : C.teal, margin: 0, fit: 'shrink' });
    s.addText(body, { x: 9.5, y: y + 0.08, w: 1.58, h: 0.18, fontSize: 8.5, color: C.ink, align: 'right', margin: 0, fit: 'shrink' });
    if (i < 2) s.addShape(pptx.ShapeType.chevron, { x: 9.47, y: y + 0.53, w: 0.34, h: 0.18, rotate: 90, fill: { color: C.teal }, line: { color: C.teal } });
  });
  s.addText('2 livelli di reasoning', { x: 1.45, y: 6.14, w: 4.35, h: 0.2, fontSize: 11.5, bold: true, color: C.red, align: 'center', margin: 0 });
  s.addText('1 reasoning loop · servizi ancora remoti', { x: 7.55, y: 6.14, w: 4.35, h: 0.2, fontSize: 11.5, bold: true, color: C.teal, align: 'center', margin: 0 });
  addSource(s, 'Diagrammi ricostruiti dal ragionamento ChatGPT condiviso; terminologia tecnica verificata sull’articolo Microsoft.', sources.reflection);
  footer(s);
  addNotes(s, "1'", `Diagrammi ricostruiti come forme native dalla conversazione condivisa: ${sources.reflection}
A sinistra il Main Agent chiama uno specialista, che esegue un proprio ciclo LLM-tool-LLM; il principale riceve testo e continua. A destra il Main Agent carica istruzioni e usa direttamente tool MCP: il reasoning resta nel principale.
È un confronto di architetture concettuali, non una garanzia di prestazioni. A2A è il protocollo; agent-as-tool è il pattern di delega. Fonte tecnica: ${sources.distributedSkills}`);
}

{
  const s = pptx.addSlide();
  addHeader(s, 'A2A vs Skill · 2/3', 'Da specialisti agentici a competenze distribuite', 'I servizi restano separati; può scomparire il modello dentro ogni specialista.');
  const domains = [
    ['WEATHER', 'meteo', C.blue, C.bluePale],
    ['SAFETY', 'sicurezza', C.red, C.redPale],
    ['SKI', 'piste', C.teal, C.pale],
    ['LIFT', 'impianti', C.amber, C.amberPale],
  ];
  s.addShape(pptx.ShapeType.roundRect, { x: 0.75, y: 2.16, w: 3.55, h: 3.95, rectRadius: 0.06, fill: { color: C.white }, line: { color: C.line } });
  s.addText('PRIMA · 5 REASONER', { x: 1.0, y: 2.4, w: 3.05, h: 0.25, fontSize: 12, bold: true, color: C.amber, align: 'center', margin: 0 });
  s.addShape(pptx.ShapeType.roundRect, { x: 1.35, y: 2.87, w: 2.35, h: 0.58, rectRadius: 0.04, fill: { color: C.navy }, line: { color: C.navy } });
  s.addText('MAIN AGENT · LLM', { x: 1.55, y: 3.05, w: 1.95, h: 0.2, fontSize: 11, bold: true, color: C.white, align: 'center', margin: 0 });
  domains.forEach(([name, , color, fill], i) => {
    const y = 3.72 + i * 0.53;
    s.addShape(pptx.ShapeType.roundRect, { x: 1.1, y, w: 2.85, h: 0.39, rectRadius: 0.03, fill: { color: fill }, line: { color, width: 1 } });
    s.addText(`${name} AGENT · LLM`, { x: 1.25, y: y + 0.1, w: 2.55, h: 0.16, fontSize: 9.5, bold: true, color, align: 'center', margin: 0 });
  });
  s.addShape(pptx.ShapeType.chevron, { x: 4.65, y: 3.69, w: 0.82, h: 0.56, fill: { color: C.mint }, line: { color: C.mint } });
  s.addText('sposta le istruzioni\nnel principale', { x: 4.42, y: 4.45, w: 1.28, h: 0.55, fontSize: 9, bold: true, color: C.teal, align: 'center', margin: 0, fit: 'shrink' });

  s.addShape(pptx.ShapeType.roundRect, { x: 5.85, y: 2.16, w: 6.73, h: 3.95, rectRadius: 0.06, fill: { color: C.white }, line: { color: C.teal, width: 1.3 } });
  s.addText('DOPO · 1 REASONER + 4 CAPABILITY', { x: 6.1, y: 2.4, w: 6.23, h: 0.25, fontSize: 12, bold: true, color: C.teal, align: 'center', margin: 0 });
  s.addShape(pptx.ShapeType.roundRect, { x: 7.82, y: 2.87, w: 2.75, h: 0.58, rectRadius: 0.04, fill: { color: C.navy }, line: { color: C.navy } });
  s.addText('MAIN AGENT · LLM', { x: 8.02, y: 3.05, w: 2.35, h: 0.2, fontSize: 11, bold: true, color: C.white, align: 'center', margin: 0 });
  domains.forEach(([name, label, color, fill], i) => {
    const y = 3.72 + i * 0.53;
    s.addShape(pptx.ShapeType.roundRect, { x: 6.45, y, w: 2.35, h: 0.39, rectRadius: 0.03, fill: { color: fill }, line: { color, width: 1 } });
    s.addText(`${name} SKILL`, { x: 6.6, y: y + 0.1, w: 2.05, h: 0.16, fontSize: 9.5, bold: true, color, align: 'center', margin: 0 });
    s.addShape(pptx.ShapeType.chevron, { x: 8.98, y: y + 0.08, w: 0.34, h: 0.22, fill: { color }, line: { color } });
    s.addShape(pptx.ShapeType.roundRect, { x: 9.5, y, w: 2.45, h: 0.39, rectRadius: 0.03, fill: { color: C.bg }, line: { color: C.line, width: 1 } });
    s.addText(`${label} MCP tools`, { x: 9.65, y: y + 0.1, w: 2.15, h: 0.16, fontSize: 9, color: C.ink, align: 'center', margin: 0, fit: 'shrink' });
  });
  s.addText('Dominio, deploy e ownership restano distribuiti.', { x: 1.2, y: 6.3, w: 10.95, h: 0.26, fontSize: 14, bold: true, color: C.ink, align: 'center', margin: 0 });
  addSource(s, 'Diagrammi “Main Agent + specialisti” e “Main Agent + Skill → MCP tools” dalla conversazione condivisa.', sources.reflection);
  footer(s);
  addNotes(s, "1'", `La conversazione mostra la trasformazione Main Agent + Weather/Safety/Ski/Lift Agent in Main Agent + quattro Skill collegate ai rispettivi MCP tools: ${sources.reflection}
Il dominio non viene centralizzato: API, dati, deploy e ownership possono restare separati. Si centralizza il reasoning quando lo specialista fornisce soprattutto procedura e operazioni.
Non convertire automaticamente gli agenti con autonomia reale, contesto privato, modello specializzato o workflow sostanziale.`);
}

{
  const s = pptx.addSlide();
  addHeader(s, 'A2A vs Skill · 3/3', 'Delego il reasoning o delego la capability?', 'La scelta dipende da autonomia e isolamento, non dal fatto che il servizio sia remoto.');
  const columns = [
    [0.75, 'AGENT-AS-TOOL · A2A', '“Delego il reasoning”', C.amber, C.amberPale,
      [['SPECIALIST AGENT', 'Model + Tools'], ['CONTEXT', 'privato e isolato'], ['STATE', 'lifecycle indipendente']]],
    [6.87, 'DISTRIBUTED SKILL · MCP', '“Delego la capability”', C.teal, C.pale,
      [['INSTRUCTIONS', 'procedura di dominio'], ['TYPED TOOLS', 'contratti operativi'], ['REMOTE SERVICE', 'business logic e dati']]],
  ];
  columns.forEach(([x, title, quote, color, fill, blocks]) => {
    s.addShape(pptx.ShapeType.roundRect, { x, y: 2.16, w: 5.72, h: 3.82, rectRadius: 0.06, fill: { color: C.white }, line: { color, width: 1.3 } });
    s.addText(title, { x: x + 0.25, y: 2.4, w: 5.22, h: 0.25, fontSize: 13, bold: true, color, align: 'center', margin: 0 });
    s.addText(quote, { x: x + 0.35, y: 2.82, w: 5.02, h: 0.3, fontSize: 17, bold: true, color: C.ink, italic: true, align: 'center', margin: 0 });
    blocks.forEach(([blockTitle, body], i) => {
      const y = 3.42 + i * 0.72;
      s.addShape(pptx.ShapeType.roundRect, { x: x + 0.78, y, w: 4.16, h: 0.55, rectRadius: 0.03, fill: { color: fill }, line: { color, width: 1 } });
      s.addText(blockTitle, { x: x + 0.95, y: y + 0.1, w: 1.65, h: 0.18, fontSize: 10, bold: true, color, margin: 0, fit: 'shrink' });
      s.addText(body, { x: x + 2.55, y: y + 0.1, w: 2.2, h: 0.18, fontSize: 9.5, color: C.ink, align: 'right', margin: 0, fit: 'shrink' });
    });
  });
  s.addText('Scegli Agent quando deve pensare autonomamente.', { x: 0.95, y: 6.18, w: 5.25, h: 0.27, fontSize: 13, bold: true, color: C.amber, align: 'center', margin: 0 });
  s.addText('Scegli Skill quando deve fornire competenza + operazioni.', { x: 7.03, y: 6.18, w: 5.25, h: 0.27, fontSize: 13, bold: true, color: C.teal, align: 'center', margin: 0 });
  addSource(s, 'Diagramma conclusivo ricostruito dalla conversazione condivisa: distributed intelligence vs distributed capability.', sources.reflection);
  footer(s);
  addNotes(s, "1'", `La conversazione sintetizza la differenza così: Agent-as-tool = delego il reasoning; Distributed Skill = delego la capability. Diagramma originale: ${sources.reflection}
Agent: autonomia, modello e strumenti propri, contesto e stato isolati. Skill: istruzioni, typed tools e servizio remoto; il Main Agent resta il reasoner.
La soluzione può essere ibrida: skill per specialisti procedurali e agent-as-tool per ricerca o task autonomi.`);
}

// Considerations from the shared reasoning and Stocchi's article.
{
  const s = pptx.addSlide();
  addHeader(s, 'Considerazioni · 1/2', 'Serve un altro agente o un’altra competenza?', 'Tool, skill e agent-as-tool distribuiscono responsabilità diverse, non soltanto chiamate HTTP.');
  s.addShape(pptx.ShapeType.roundRect, { x: 0.7, y: 2.12, w: 2.67, h: 4.12, rectRadius: 0.06, fill: { color: C.white }, line: { color: C.line } });
  s.addImage({ path: stocchiPhoto, x: 1.485, y: 2.35, w: 1.1, h: 1.1, altText: 'Tommaso Stocchi — foto del profilo Microsoft Developer Blogs', hyperlink: { url: sources.author } });
  s.addText('Tommaso Stocchi', { x: 0.89, y: 3.66, w: 2.29, h: 0.3, fontSize: 15, bold: true, color: C.ink, align: 'center', margin: 0, hyperlink: { url: sources.author } });
  s.addText('Microsoft Developer Blogs\n16 settembre 2026', { x: 0.91, y: 4.06, w: 2.25, h: 0.51, fontSize: 10.5, color: C.muted, align: 'center', margin: 0 });
  s.addText('From Specialist Agents\nto Distributed Skills\nover MCP', { x: 0.93, y: 4.83, w: 2.21, h: 0.79, fontSize: 12, bold: true, color: C.teal, align: 'center', margin: 0, hyperlink: { url: sources.distributedSkills } });
  s.addText('Foto: profilo autore Microsoft\nLink a fonte e profilo cliccabili', { x: 0.88, y: 5.77, w: 2.31, h: 0.3, fontSize: 8, color: C.muted, align: 'center', margin: 0, hyperlink: { url: sources.author } });
  const choices = [
    ['TOOL', 'Esegue un’operazione delimitata.\nLa decisione su quando usarlo resta al chiamante.', C.blue, C.bluePale],
    ['SKILL DISTRIBUITA', 'Porta una procedura nel contesto del principale.\nNell’articolo: istruzioni + tool remoti via MCP, senza un LLM specialista.', C.teal, C.pale],
    ['AGENT-AS-TOOL', 'Delega un sottoproblema a un altro reasoner.\nUtile per autonomia, contesto privato, modello o workflow specializzato.', C.amber, C.amberPale],
  ];
  choices.forEach(([title, body, color, fill], i) => {
    const y = 2.12 + i * 1.36;
    s.addShape(pptx.ShapeType.roundRect, { x: 3.65, y, w: 8.95, h: 1.2, rectRadius: 0.05, fill: { color: fill }, line: { color, width: 1 } });
    s.addText(title, { x: 3.88, y: y + 0.28, w: 2.08, h: 0.61, fontSize: 12.5, bold: true, color, margin: 0, valign: 'mid', fit: 'shrink' });
    s.addText(body, { x: 6.12, y: y + 0.21, w: 6.22, h: 0.78, fontSize: 12, color: C.ink, margin: 0, valign: 'mid', fit: 'shrink' });
  });
  s.addText('Servizi e ownership possono restare distribuiti anche con un solo reasoner principale.', { x: 3.65, y: 6.24, w: 8.95, h: 0.29, fontSize: 12.5, bold: true, color: C.ink, align: 'center', margin: 0, fit: 'shrink' });
  s.addText([
    { text: 'Fonte: Tommaso Stocchi · articolo e foto Microsoft Developer Blogs', options: { hyperlink: { url: sources.distributedSkills } } },
    { text: '  |  Ragionamento ChatGPT condiviso', options: { hyperlink: { url: sources.reflection } } },
  ], { x: 0.68, y: 6.72, w: 11.9, h: 0.2, fontSize: 7.5, color: '718690', italic: true, margin: 0, fit: 'shrink' });
  footer(s);
  addNotes(s, "1'", `Ragionamento di partenza fornito dall'utente, letto nella pagina pubblica: ${sources.reflection}
Sintesi/parafrasi, non riproduzione integrale: tool = operazione delimitata; skill = competenza/procedura acquisita dal principale; agent-as-tool = delega a un reasoner autonomo. Il numero di processi non determina il numero di cicli LLM.
Fonte tecnica: Tommaso Stocchi, From Specialist Agents to Distributed Skills over MCP, Microsoft Developer Blogs, 16/09/2026: ${sources.distributedSkills}
La tesi è mantenere distribuiti i servizi di dominio e spostare le istruzioni dello specialista nell'orchestratore quando non serve un altro reasoner. Non significa sostituire qualsiasi agente con una skill: nell'articolo un agente di ricerca resta agent-as-tool in entrambe le architetture.
Autonomia, contesto privato, modello specifico e workflow complesso sono buoni motivi per mantenere uno specialista agente. Un deployment indipendente non richiede da solo un LLM aggiuntivo: anche un provider di skill o un servizio business può avere infrastruttura e ownership proprie.
DISTINZIONE DALLA NOSTRA DEMO: qui le skill sono pubblicate via API HTTP ad hoc e i tool sono nel router. L'articolo distribuisce istruzioni e operazioni tramite MCP. Non affermare che la nostra demo implementi MCP.
Il trasporto skill mostrato nell'articolo usa una revisione storica di SEP-2640 con skill://index.json; non è una proprietà obbligatoria del core MCP. Il codice citato usa agent-framework-core 1.17.0 e una specifica API sperimentale: verificare versioni/compatibilità prima di migrare.
ATTRIBUZIONE FOTO: profilo pubblico di Tommaso Stocchi, ${sources.author}
Immagine originale 150×150: ${sources.photo}
La foto inclusa è una conversione PNG dell'immagine pubblicata, non generata. La pubblicazione sul web non implica una licenza libera di riutilizzo.`);
}

{
  const s = pptx.addSlide();
  addHeader(s, 'Considerazioni · 2/2', 'Dialogo tra agenti: chi mantiene il controllo?', 'Agent-as-tool è adatto a risposte mirate e responsabilità chiare; la velocità va misurata.');
  const dialogue = [
    ['Delega · agent-as-tool', 'Principale → specialista → principale\nResponsabilità del sottotask allo specialista; risposta finale al principale.', C.teal, C.pale],
    ['Trasferimento · handoff', 'Agente A → agente B\nIl destinatario assume il task e prosegue il dialogo.', C.blue, C.bluePale],
    ['Collaborazione · group chat / Magentic', 'Manager ↔ agenti\nContesto condiviso, turni o piano adattivo; più coordinamento da misurare.', C.amber, C.amberPale],
  ];
  dialogue.forEach(([title, body, color, fill], i) => {
    const y = 2.12 + i * 1.17;
    s.addShape(pptx.ShapeType.roundRect, { x: 0.7, y, w: 7.43, h: 1.04, rectRadius: 0.05, fill: { color: fill }, line: { color, width: 1 } });
    s.addText(title, { x: 0.9, y: y + 0.15, w: 7.03, h: 0.25, fontSize: 13, bold: true, color, margin: 0, fit: 'shrink' });
    s.addText(body, { x: 0.9, y: y + 0.48, w: 7.03, h: 0.44, fontSize: 10.5, color: C.ink, margin: 0, fit: 'shrink' });
  });
  s.addShape(pptx.ShapeType.roundRect, { x: 8.4, y: 2.12, w: 4.23, h: 3.85, rectRadius: 0.05, fill: { color: C.white }, line: { color: C.line } });
  s.addText('NELLA DEMO DI STOCCHI', { x: 8.61, y: 2.31, w: 3.81, h: 0.22, fontSize: 10, bold: true, color: C.teal, charSpacing: 0.7, margin: 0 });
  s.addText('Tre coppie di run, non un benchmark', { x: 8.61, y: 2.65, w: 3.81, h: 0.25, fontSize: 11, bold: true, color: C.ink, margin: 0, fit: 'shrink' });
  const rows = [
    ['MISURA', 'A2A', 'SKILL MCP'],
    ['Media end-to-end', '15,480 s', '6,348 s'],
    ['Chiamate / run', '6 / 6 / 7', '3 / 3 / 3'],
    ['Token totali (3 run)', '11.134', '13.533'],
  ];
  rows.forEach((row, ri) => row.forEach((value, ci) => {
    const widths = [1.69, 1.0, 1.12];
    const x = 8.61 + widths.slice(0, ci).reduce((sum, w) => sum + w, 0);
    const y = 3.09 + ri * 0.37;
    s.addShape(pptx.ShapeType.rect, { x, y, w: widths[ci], h: 0.37, fill: { color: ri === 0 ? C.navy : ri % 2 ? C.bg : C.white }, line: { color: C.white, width: 0.5 } });
    s.addText(value, { x: x + 0.04, y: y + 0.1, w: widths[ci] - 0.08, h: 0.17, fontSize: 8.5, bold: ri === 0, color: ri === 0 ? C.white : C.ink, margin: 0, align: ci === 0 ? 'left' : 'center', fit: 'shrink' });
  }));
  s.addText('Skill più rapide qui, ma +22% token.\nCache e lavoro non equivalenti.\nNessuna garanzia su qualità o costo.', { x: 8.61, y: 4.81, w: 3.81, h: 0.81, fontSize: 11, bold: true, color: C.red, margin: 0, fit: 'shrink' });
  s.addText('PATTERN ≠ PROTOCOLLO', { x: 0.7, y: 5.79, w: 7.43, h: 0.22, fontSize: 10, bold: true, color: C.teal, charSpacing: 0.7, margin: 0 });
  s.addText('A2A: interoperabilità tra agenti · MCP: accesso a tool e risorse.\nMettere un agente dietro MCP non elimina il suo ciclo LLM.', { x: 0.7, y: 6.08, w: 11.9, h: 0.47, fontSize: 12, color: C.ink, margin: 0, fit: 'shrink' });
  s.addText([
    { text: 'Fonti: Stocchi · From Specialist Agents to Distributed Skills over MCP', options: { hyperlink: { url: sources.distributedSkills } } },
    { text: '  |  Microsoft Learn · Handoff vs agent-as-tools', options: { hyperlink: { url: sources.handoff } } },
  ], { x: 0.68, y: 6.72, w: 11.9, h: 0.2, fontSize: 7.5, color: '718690', italic: true, margin: 0, fit: 'shrink' });
  footer(s);
  addNotes(s, "1'", `PATTERN DI DIALOGO: agent-as-tool = delega con ritorno; handoff = trasferimento della responsabilità del task; group chat = turni in una conversazione condivisa; Magentic aggiunge pianificazione e verifica adattiva. Sequential e Concurrent, nella mappa iniziale, coordinano passaggio ordinato o lavoro indipendente in parallelo.
Agent-as-tool è un'ottima scelta per un sottotask con input/output chiari e responsabilità specialistica: il principale conserva il controllo complessivo e decide la risposta finale. Un modello adeguato, contesto limitato, tool pertinenti e deleghe indipendenti in parallelo possono aiutare la latenza; NON garantiscono che sia più veloce di un singolo agente. Ogni specialista può aggiungere inferenze e round-trip. Il contratto deve stabilire risultato atteso, errori, consenso e confini delle operazioni. Autorizzazione e validazione devono essere nel codice.
Confronto ufficiale: ${sources.handoff}
DATI ESTERNI, NON MISURE AI OBSERVATORY: ${sources.distributedSkills}
L'articolo riporta tre coppie: A2A 6/6/7 chiamate modello contro MCP skills 3/3/3; media end-to-end 15,480 s contro 6,348 s; token osservati complessivi 11.134 contro 13.533 (circa +22%). Non sono tariffe o costo in USD. Le metriche sono tempo fino a completamento SSE, non TTFT.
Tre coppie illustrative, non studio controllato: processi già avviati, cache variabile, inizializzazione credenziali, runtime diversi, lavoro non identico e contatori cache A2A incompleti. Le chiamate degli specialisti si sovrappongono: non disegnare tutte le inferenze come sequenziali.
Qualità: alcune risposte offrivano rassicurazioni senza consultare il provider safety. La latenza inferiore non dimostra stessa correttezza o completezza.
A2A e MCP sono protocolli, non orchestrazioni. Nell'articolo A2A implementa agent-as-tool; MCP distribuisce skill e operazioni. Rimane anche un agente di ricerca in entrambe le soluzioni: l'ibrido è legittimo. La nostra demo Skills usa HTTP, non MCP, e le misure della nostra demo restano separate.
Collegamento al ragionamento condiviso: ${sources.reflection}`);
}

// 5
{
  const s = pptx.addSlide();
  addHeader(s, 'Prompting', '5.4 → 5.6 → 6: cambia il contratto, non solo il nome', 'Le guide evolvono da controllo della forma a obiettivi, prove e autonomia.');
  const cols = [
    ['GPT-5.4', 'Forma e dettaglio', ['Guida la verbosità', 'Separa dettaglio da reasoning', 'Adatta la risposta al task'], C.blue, C.bluePale],
    ['GPT-5.6', 'Esito e prove', ['Dichiara risultato e vincoli', 'Chiedi evidenze e stop condition', 'Rimuovi ridondanza per gruppi'], C.teal, C.pale],
    ['GPT-6', 'Autonomia governata', ['Dichiara margine di autonomia', 'Chiarisci quando chiedere', 'Controlla skill e priorità'], C.amber, C.amberPale],
  ];
  cols.forEach((c, i) => {
    const x = 0.72 + i * 4.18;
    card(s, x, 2.2, 3.72, 3.95, c[0], `${c[2][0]}\n\n${c[2][1]}\n\n${c[2][2]}`, { kicker: c[1], fill: c[4], line: c[3], accent: c[3], titleSize: 22, bodySize: 13 });
  });
  s.addText('Non migrare i prompt con “trova e sostituisci”: rivaluta istruzioni, tool e criteri di completamento.', { x: 1.15, y: 6.36, w: 11, h: 0.32, fontSize: 15, bold: true, color: C.navy, align: 'center', margin: 0 });
  addSource(s, 'Fonti: guide ufficiali OpenAI GPT-5.4, GPT-5.6 Sol e GPT-6, verificate il 24–25/09/2026.');
  footer(s);
  addNotes(s, "3'", "Messaggio: non esiste il prompt universalmente ottimale. Per 5.4 evidenziare la gestione esplicita della verbosità; per 5.6 partire da risultato/prove/vincoli; per GPT-6 governare autonomia, chiarimenti, skill e priorità. Non confondere istruzioni di verbosità con reasoning effort.");
}

// 6
{
  const s = pptx.addSlide();
  addHeader(s, 'Famiglia GPT-6', 'Astra, Sol, Luna… e Terra?', 'Stessa famiglia non significa stesso profilo operativo.');
  const models = [
    ['ASTRA', 'Massima capacità', 'Lavoro end-to-end difficile\nreasoning, coding, ricerca', '$10 / $1 / $50', C.redPale, C.red],
    ['SOL', 'Agentic & coding', 'Workflow complessi\nstrumenti e contesto lungo', '$2 / $0,2 / $10', C.amberPale, C.amber],
    ['LUNA', 'Efficienza', 'Task focalizzati\nalto volume', '$0,1 / $0,01 / $0,5', C.pale, C.teal],
    ['TERRA', 'Non verificato', 'Nessuna model card ufficiale\nreperita al 25/09/2026', 'NON ASSUMERE', 'EEF2F4', C.muted],
  ];
  models.forEach((m, i) => {
    const x = 0.55 + i * 3.18;
    card(s, x, 2.18, 2.85, 3.98, m[0], `${m[2]}\n\nTariffe listino:\n${m[3]}`, { kicker: m[1], fill: m[4], line: m[5], accent: m[5], titleSize: 21, bodySize: 12.5 });
    pill(s, x + 0.35, 5.55, 2.15, i < 3 ? 'INPUT / CACHE / OUTPUT' : 'VERIFICA PRIMA', m[5], C.white);
  });
  s.addText('Scegli per task + SLA + qualità accettabile. Non dal prezzo per token isolato.', { x: 1.45, y: 6.36, w: 10.45, h: 0.34, fontSize: 17, bold: true, color: C.navy, align: 'center', margin: 0 });
  addSource(s, 'Listino OpenAI Standard per 1M token, 25/09/2026. Cache write esclusa dalla terna. Oltre 272K input: tier maggiorato. Verificare sempre Azure/deployment.');
  footer(s);
  addNotes(s, "3'", "Astra: più capace per lavori difficili; Sol: complessi workflow agentici/coding; Luna: efficienza per task focalizzati e volume. Terra: la richiesta lo cita, ma la model card ufficiale non è disponibile: non inventare differenze. Nella demo LIVE sono verificati GPT-5 e Sol; Astra/Luna non sono ancora live-ready. Le tariffe mostrate sono orientative del listino OpenAI, non la fattura Azure.");
}

// 7
{
  const s = pptx.addSlide();
  addHeader(s, 'Prompt design', 'Un prompt utile è un contratto osservabile', 'Riduci ambiguità prima di ridurre token.');
  const blocks = [
    ['1', 'RISULTATO', 'Cosa deve essere vero alla fine?'],
    ['2', 'CONTESTO', 'Quali fatti servono davvero?'],
    ['3', 'VINCOLI', 'Policy, sicurezza, consenso, budget'],
    ['4', 'PROVE', 'Fonti, tool, dati da citare'],
    ['5', 'STOP', 'Quando completare o chiedere chiarimenti'],
    ['6', 'FORMATO', 'Struttura e livello di dettaglio'],
  ];
  blocks.forEach((b, i) => {
    const x = 0.7 + (i % 3) * 4.2;
    const y = 2.18 + Math.floor(i / 3) * 1.65;
    s.addShape(pptx.ShapeType.roundRect, { x, y, w: 3.75, h: 1.28, rectRadius: 0.05, fill: { color: C.white }, line: { color: C.line } });
    s.addShape(pptx.ShapeType.ellipse, { x: x + 0.22, y: y + 0.28, w: 0.62, h: 0.62, fill: { color: C.navy }, line: { color: C.navy } });
    s.addText(b[0], { x: x + 0.4, y: y + 0.47, w: 0.26, h: 0.16, fontSize: 11, bold: true, color: C.mint, align: 'center', margin: 0 });
    s.addText(b[1], { x: x + 1.02, y: y + 0.22, w: 2.45, h: 0.24, fontSize: 12, bold: true, color: C.teal, margin: 0, charSpacing: 0.8 });
    s.addText(b[2], { x: x + 1.02, y: y + 0.61, w: 2.45, h: 0.42, fontSize: 11.5, color: C.ink, margin: 0, fit: 'shrink' });
  });
  s.addText('Tagliare prima di chiarire = risparmiare token per ottenere più retry.', { x: 2.15, y: 5.82, w: 9, h: 0.45, fontSize: 21, bold: true, color: C.red, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "2'", "Usare questa slide come checklist di prompt review. Un prompt lungo non è necessariamente cattivo; un prompt breve non è necessariamente economico se genera chiamate inutili, chiarimenti o output errati.");
}

// 8
{
  const s = pptx.addSlide();
  addHeader(s, 'Economia dei token', 'Input, cache e output non costano allo stesso modo', 'Il ledger deve conservare contatori e tariffa applicata per ogni chiamata.');
  const widths = [5.5, 2.0, 1.2, 2.25];
  const labels = [
    ['INPUT NUOVO', 'istruzioni dinamiche · history · tool result', C.blue],
    ['INPUT IN CACHE', 'prefisso stabile riusato', C.teal],
    ['CACHE WRITE', 'prefisso scritto', C.amber],
    ['OUTPUT', 'risposta + reasoning incluso', C.red],
  ];
  let x = 0.8;
  labels.forEach((l, i) => {
    s.addShape(pptx.ShapeType.rect, { x, y: 2.45, w: widths[i], h: 1.05, fill: { color: l[2] }, line: { color: C.white, width: 2 } });
    s.addText(l[0], { x: x + 0.08, y: 2.68, w: widths[i] - 0.16, h: 0.22, fontSize: widths[i] < 1.5 ? 8.5 : 11, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
    s.addText(l[1], { x, y: 3.72, w: widths[i], h: 0.55, fontSize: 10.5, color: C.muted, align: 'center', margin: 0, fit: 'shrink' });
    x += widths[i];
  });
  card(s, 0.82, 4.62, 3.75, 1.42, 'Cache hit', 'Riduce il costo del prefisso già noto. Non riduce automaticamente output, tool o retry.', { fill: C.pale, line: C.teal, titleSize: 15, bodySize: 10.5, shadow: false });
  card(s, 4.8, 4.62, 3.75, 1.42, 'Cache write', 'Può avere una tariffa propria. Nel ledger Sol sostituisce il normale costo input.', { fill: C.amberPale, line: C.amber, titleSize: 15, bodySize: 10.5, shadow: false });
  card(s, 8.78, 4.62, 3.75, 1.42, 'Reasoning', 'È un sottoinsieme dell’output: non sommarlo una seconda volta.', { fill: C.redPale, line: C.red, titleSize: 15, bodySize: 10.5, shadow: false });
  footer(s);
  addNotes(s, "3'", "Spiegare i bucket senza doppio conteggio. Input totale può includere cached e cache write; reasoning è incluso nell'output. Un valore assente non vale zero. La semantica del provider e della rate card deve essere congelata nella run.");
}

// 9
{
  const s = pptx.addSlide();
  addHeader(s, 'Prompt caching', 'La cache premia la stabilità del prefisso', 'Metti il contenuto stabile prima; sposta il dinamico dopo.');
  const before = ['SYSTEM / POLICY', 'TOOLS', 'ESEMPI STABILI', 'HISTORY', 'DOMANDA'];
  before.forEach((t, i) => {
    const w = [2.25, 1.2, 2.0, 2.2, 1.6][i];
    const prev = before.slice(0, i).reduce((sum, _, j) => sum + [2.25, 1.2, 2.0, 2.2, 1.6][j], 0);
    s.addShape(pptx.ShapeType.roundRect, { x: 1.2 + prev, y: 2.62, w: w, h: 0.85, rectRadius: 0.03, fill: { color: i < 3 ? C.teal : (i === 3 ? C.blue : C.amber) }, line: { color: C.white, width: 2 } });
    s.addText(t, { x: 1.24 + prev, y: 2.92, w: w - 0.08, h: 0.18, fontSize: 9, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
  });
  s.addShape(pptx.ShapeType.line, { x: 1.25, y: 3.78, w: 5.35, h: 0, line: { color: C.teal, width: 3, beginArrowType: 'none', endArrowType: 'triangle' } });
  s.addText('PREFISSO RIUSABILE', { x: 2.7, y: 3.98, w: 2.5, h: 0.22, fontSize: 10, bold: true, color: C.teal, align: 'center', margin: 0, charSpacing: 1 });
  const tips = [
    ['Stabile', 'system, policy e tool schema'],
    ['Identico', 'non cambiare spazi/versioni inutilmente'],
    ['Lungo abbastanza', 'verificare soglie e comportamento provider'],
    ['Misurato', 'cache hit rate, costo e latenza'],
  ];
  tips.forEach((t, i) => card(s, 0.75 + i * 3.13, 4.68, 2.8, 1.18, t[0], t[1], { fill: C.white, titleSize: 13.5, bodySize: 10, shadow: false }));
  s.addText('Cache miss ≠ bug. È un segnale da spiegare.', { x: 8.5, y: 3.8, w: 3.3, h: 0.35, fontSize: 16, bold: true, color: C.ink, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "3'", "Buone pratiche: prefisso stabile e identico, dati dinamici in coda, versionare prompt/tool, osservare hit rate. Non gonfiare il prompt solo per inseguire la cache. Una cache miss è spesso spiegabile da piccole variazioni o soglie.");
}

// 10
{
  const s = pptx.addSlide();
  addHeader(s, 'Contabilità', 'Il costo corretto è una formula per chiamata', 'Poi si aggrega per run, conversazione ed esito.');
  s.addShape(pptx.ShapeType.roundRect, { x: 0.85, y: 2.35, w: 11.65, h: 1.42, rectRadius: 0.05, fill: { color: C.navy }, line: { color: C.navy } });
  s.addText('((nuovo input × Rᵢ) + (cached × R꜀) + (write × Rw) + (output × Rₒ)) / 1.000.000', { x: 1.2, y: 2.83, w: 10.95, h: 0.4, fontSize: 20, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
  const qs = [
    ['CHIAMATA', 'Quanto costa questa inferenza?'],
    ['RUN', 'Tool + retry + deleghe?'],
    ['CONVERSAZIONE', 'Quanto costa il percorso completo?'],
    ['ESITO', 'Quanto costa una risposta corretta?'],
  ];
  qs.forEach((q, i) => card(s, 0.72 + i * 3.13, 4.25, 2.82, 1.48, q[0], q[1], { fill: i === 3 ? C.pale : C.white, line: i === 3 ? C.teal : C.line, titleSize: 13, bodySize: 11, shadow: false }));
  s.addText('Costo assente = sconosciuto, non zero.', { x: 3.65, y: 6.13, w: 6.1, h: 0.42, fontSize: 20, bold: true, color: C.red, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "3'", "Il nuovo input è input meno cached e cache writes quando i bucket sono inclusivi. Il ledger dell'app fail-closed se usage o pricing sono incompleti. Distinguere costo stimato dalla fattura Azure e budget applicativo da tetto provider.");
}

// 11
{
  const s = pptx.addSlide();
  addHeader(s, 'Latenza', 'Prompt lungo: costo certo, latenza da misurare', 'La durata end-to-end è composta da più segmenti.');
  const segs = [
    ['QUEUE', 0.9, C.muted],
    ['INPUT', 2.3, C.blue],
    ['REASONING', 1.9, C.amber],
    ['TOOLS', 2.6, C.teal],
    ['OUTPUT', 1.45, C.red],
  ];
  let x = 1.25;
  segs.forEach(seg => {
    s.addShape(pptx.ShapeType.rect, { x, y: 2.45, w: seg[1], h: 0.72, fill: { color: seg[2] }, line: { color: C.white, width: 1.5 } });
    s.addText(seg[0], { x, y: 2.71, w: seg[1], h: 0.16, fontSize: 9, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
    x += seg[1];
  });
  s.addText('tempo end-to-end', { x: 1.25, y: 3.4, w: 9.15, h: 0.22, fontSize: 10, bold: true, color: C.muted, align: 'center', charSpacing: 1.2, margin: 0 });
  const trade = [
    ['Più contesto', '+ prefill · + costo\nma meno lookup mancanti?'],
    ['Più spiegazione', '+ output · + tempo\nma più chiarezza?'],
    ['Più autonomia', '+ tool/retry possibili\nma meno handoff umano?'],
  ];
  trade.forEach((t, i) => card(s, 0.88 + i * 4.18, 4.42, 3.72, 1.45, t[0], t[1], { fill: C.white, titleSize: 15, bodySize: 11.5, shadow: false }));
  s.addText('Nella demo la “prima risposta” è bufferizzata: non chiamarla TTFT del provider.', { x: 2.15, y: 6.2, w: 9.1, h: 0.3, fontSize: 13, bold: true, color: C.red, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "3'", "Scomporre la latenza. Prompt e output più lunghi tendono ad aumentare lavoro e costo, ma il trade-off reale va misurato. Nell'app la risposta è bufferizzata: il tempo alla prima risposta non equivale al time-to-first-token streaming del provider.");
}

// 12
{
  const s = pptx.addSlide();
  addHeader(s, 'Esperimenti', 'Cambiare una variabile. Ripetere. Conservare evidenza.', 'BAD vs GOOD, GOOD vs ridondante, GPT-5 vs GPT-6 Sol.');
  const steps = [
    ['01', 'FISSA', 'scenario · dati · limiti'],
    ['02', 'VARIA', 'un solo fattore'],
    ['03', 'RIPETI', '≥ 3 run · ordine alternato'],
    ['04', 'VALUTA', 'rubrica + telemetria'],
    ['05', 'CONSERVA', 'prompt · run · tariffa'],
  ];
  steps.forEach((st, i) => {
    const x = 0.65 + i * 2.53;
    s.addShape(pptx.ShapeType.ellipse, { x: x + 0.75, y: 2.28, w: 0.82, h: 0.82, fill: { color: i === 4 ? C.teal : C.navy }, line: { color: C.white, width: 2 } });
    s.addText(st[0], { x: x + 0.93, y: 2.56, w: 0.46, h: 0.18, fontSize: 11, bold: true, color: C.mint, align: 'center', margin: 0 });
    s.addText(st[1], { x, y: 3.37, w: 2.3, h: 0.22, fontSize: 11, bold: true, color: C.teal, align: 'center', charSpacing: 1, margin: 0 });
    s.addText(st[2], { x, y: 3.82, w: 2.3, h: 0.52, fontSize: 11.5, color: C.ink, align: 'center', margin: 0, fit: 'shrink' });
    if (i < 4) s.addShape(pptx.ShapeType.chevron, { x: x + 2.24, y: 2.56, w: 0.28, h: 0.22, fill: { color: C.mint }, line: { color: C.mint } });
  });
  card(s, 1.2, 4.82, 3.35, 1.25, 'A/B', 'BAD → GOOD\ncambia il profilo prompt', { fill: C.bluePale, line: C.blue, titleSize: 14, bodySize: 10.5, shadow: false });
  card(s, 4.98, 4.82, 3.35, 1.25, 'B/C', 'GOOD → ridondante\ncambia un solo blocco', { fill: C.amberPale, line: C.amber, titleSize: 14, bodySize: 10.5, shadow: false });
  card(s, 8.75, 4.82, 3.35, 1.25, 'MODELLO', 'GPT-5 → GPT-6 Sol\nstesso task e limiti', { fill: C.pale, line: C.teal, titleSize: 14, bodySize: 10.5, shadow: false });
  footer(s);
  addNotes(s, "2'", "Spiegare che A/C non è un confronto a una variabile. Usare chat nuove per ogni variante. Alternare ordine per ridurre bias temporali. Una singola risposta non crea una classifica.");
}

// 13
{
  const s = pptx.addSlide();
  addHeader(s, 'Demo LIVE · protocollo senza cap applicativi', 'Una domanda, tre architetture, due ripetizioni', 'GPT-5 / GOOD · chat nuove · full history · direct · nessun override · blocchi opzionali spenti.');
  card(s, 0.75, 2.12, 6.4, 3.25, 'ORD-1042', measurements.experiment.question, {
    kicker: 'DOMANDA IDENTICA IN TUTTE LE SEI RUN', bodySize: 16, fill: C.white,
  });
  s.addText(`Nessun cap applicativo: output, call, budget.\n${measurements.protocol.order}\nCache non azzerata né controllata; limiti tecnici provider/SDK presenti.`, {
    x: 0.95, y: 5.45, w: 6.05, h: 0.65, fontSize: 10, color: C.muted, margin: 0, fit: 'shrink',
  });
  card(s, 7.62, 2.12, 4.75, 3.95, 'Cosa guardiamo', '① configurazione effettiva\n② tool e agenti invocati\n③ numero di chiamate e retry\n④ input / cached / output\n⑤ durata e costo per run\n⑥ fatti e policy della risposta', { kicker: 'NON SOLO LA CHAT', fill: C.navy, line: C.navy, titleColor: C.white, bodyColor: C.white, accent: C.mint, bodySize: 14 });
  pill(s, 0.92, 6.2, 1.25, 'LIVE', C.red, C.redPale);
  s.addText('unboundedExecution=true · budget=null · MaxOutputTokens effettivo=null · nessuna bozza', { x: 2.35, y: 6.27, w: 10, h: 0.22, fontSize: 10.5, color: C.muted, margin: 0, fit: 'shrink' });
  footer(s);
  addNotes(s, "7'", `DEMO. Mostrare dallo Storico le sei run già persistite e le loro tracce, senza doverle rieseguire tutte sul palco. Se si esegue una nuova prova, usare la domanda e la configurazione esatte; consenso manuale prima dell'invio. Il percorso misurato è un solo turno per chat, non la conversazione multi-turn originaria.
Configurazione: ${JSON.stringify(measurements.experiment.configuration)}
Domanda: ${measurements.experiment.question}
Snapshot catalogo: ${measurements.catalogHash}
${measurements.protocol.applicationCaps}
${measurements.protocol.compatibilityNotice}
Il massimo di output non è inviato al modello: request.parameters.maxOutputTokens=null in tutte le chiamate. I valori compatibilità 4096 e 24 NON sono limiti applicati.
Le sei run sono sequenziali, nello stesso ordine mostrato. Nessun intervento fra le ripetizioni.
Cache non azzerata né controllata: prima prova non significa cold, seconda non significa warm.
${runReferences}`);
}

// 14
{
  const s = pptx.addSlide();
  addHeader(s, `Misure LIVE · ${measurementDay}`, 'Sei run reali: token, latenza, costo ed esito', 'GPT-5 / GOOD · stessa domanda · un agente per processo, skill remote · nessun cap applicativo su output, call o budget');
  const headers = ['CASO', 'CALL', 'INPUT', 'CACHED¹', 'OUTPUT²', 'REASONING²', 'SECONDI', 'USD', 'ESITO'];
  const w = [1.2, 0.6, 1.32, 1.32, 1.32, 1.35, 1.05, 1.45, 2.4];
  const x = w.map((_, i) => 0.65 + w.slice(0, i).reduce((a, b) => a + b, 0));
  headers.forEach((h, i) => {
    s.addShape(pptx.ShapeType.rect, { x: x[i], y: 2.15, w: w[i], h: 0.48, fill: { color: C.navy }, line: { color: C.white, width: 1 } });
    s.addText(h, { x: x[i] + 0.04, y: 2.31, w: w[i] - 0.08, h: 0.18, fontSize: 9, bold: true, color: C.white, align: 'center', margin: 0, fit: 'shrink' });
  });
  measuredRows.forEach((r, ri) => {
    const values = [
      `${r.architecture === 'a2a' ? 'A2A' : r.architecture === 'skills' ? 'Skills' : 'Inline'} ${r.repetition}`,
      String(r.calls), formatNumber(r.input), formatNumber(r.cached), formatNumber(r.output),
      formatNumber(r.reasoning), formatNumber(r.durationMs / 1000, 2), formatNumber(r.costUsd, 8),
      `${r.status} · ${r.toolFailures} err. tool`,
    ];
    values.forEach((value, i) => {
      const y = 2.63 + ri * 0.45;
      s.addShape(pptx.ShapeType.rect, { x: x[i], y, w: w[i], h: 0.45, fill: { color: r.status === 'failed' ? C.redPale : ri % 2 ? 'F7FAFA' : C.white }, line: { color: C.line, width: 0.8 } });
      s.addText(value, { x: x[i] + 0.05, y: y + 0.14, w: w[i] - 0.1, h: 0.19, fontSize: 11, bold: i === 0 || i === 8, color: i === 8 && r.status === 'failed' ? C.red : C.ink, align: i === 0 ? 'left' : 'center', margin: 0, fit: 'shrink' });
    });
  });
  const cacheWrites = measuredRows.every(row => row.cacheWrite == null) ? 'N/D' : measuredRows.map(row => formatNumber(row.cacheWrite)).join(' / ');
  s.addText(`¹ Cached incluso nell’input.  ² Output include reasoning e output intermedi, non solo risposta visibile.\nCache write: ${cacheWrites}. Durata end-to-end, non TTFT. Cache non controllata. Due giri non sono un benchmark.`, {
    x: 0.7, y: 5.53, w: 11.9, h: 0.55, fontSize: 11, color: C.muted, margin: 0,
  });
  s.addText(`Sei run: ${formatNumber(measurements.totalControlledCostUsd, 8)} USD · ${currentSummary}`, {
    x: 0.7, y: 6.2, w: 11.9, h: 0.32, fontSize: 13, bold: true, color: C.teal, margin: 0, fit: 'shrink',
  });
  addSource(s, `Fonte: sei export LIVE/provider del ${measurementDate.toLocaleDateString('it-IT')} (${path.basename(measurementsDirectory)}). Run ID e contatori non arrotondati nelle note. Stime ledger, non fattura.`);
  footer(s);
  addNotes(s, "3'", `Leggere i dati, non proclamare un vincitore. Esiti correnti: ${currentSummary}. Completed non certifica la correttezza dei fatti o il successo di tutti i tool. Un eventuale failed include solo il lavoro precedente allo stop: non è efficienza a parità di esito.
La cache non è stata controllata; non attribuire causalmente alla cache le differenze fra i giri.
Questo totale comprende solo le sei run senza cap applicativi, non pilot né precedenti run con budget. Restano limiti tecnici provider/SDK.
ASSERTIONS VERIFICATE: ${JSON.stringify(measurements.assertions)}
${measuredRows.map(r => JSON.stringify({ ...r, answer: undefined, errors: undefined, sources: undefined, tools: undefined })).join('\n')}`);
}

// 15
{
  const s = pptx.addSlide();
  addHeader(s, 'Prompt effettivi', 'Stesso profilo GOOD ≠ stesso contesto del modello', 'Dimensioni delle istruzioni catturate: caratteri, non token. I testi completi sono nelle note.');
  ['inline','skills','a2a'].forEach((architecture, index) => {
    const runs = measuredRows.filter(row => row.architecture === architecture);
    const agents = [...new Set(runs.flatMap(row => row.agents))];
    const counts = agents.map(agent => `${agent}: ${[...new Set(runs.flatMap(row => row.promptCharacters[agent] ?? []))].map(count => formatNumber(count)).join(' / ')} car.`).join('\n');
    const description = architecture === 'inline' ? 'Istruzioni di dominio inline.' :
      architecture === 'skills' ? 'Skill e risultati entrano nel contesto.' : 'Istruzioni degli agenti invocati.';
    card(s, [0.7,4.8,8.9][index], 2.18, 3.75, 3.95, architectureLabel(architecture),
      `${counts}\n\n${description}\n\nCall prova 1 / 2: ${runs.map(row => row.calls).join(' / ')}.`, {
        kicker: ['ISTRUZIONI NEL ROUTER','CARICAMENTO ON DEMAND','ISTRUZIONI PER AGENTE'][index],
        fill:[C.bluePale,C.pale,C.amberPale][index], line:[C.blue,C.teal,C.amber][index],
        accent:[C.blue,C.teal,C.amber][index], bodySize:14, titleSize:23,
      });
  });
  s.addText('Input fatturato = istruzioni + messaggi + schemi tool + risultati + contesto runtime, per ogni chiamata.', {
    x: 0.75, y: 6.32, w: 11.9, h: 0.3, fontSize: 13, bold: true, color: C.ink, margin: 0, fit: 'shrink',
  });
  addSource(s, 'Fonte: request.instructions delle chiamate effettive. Cattura logica, non wire; non misura separatamente i token del system prompt.');
  footer(s);
  const promptGroups = new Map();
  for (const bundle of evidence) {
    for (const call of bundle.ledger.calls) {
      const key = JSON.stringify([bundle.run.technology, call.agent, call.request.instructions]);
      if (!promptGroups.has(key)) promptGroups.set(key, {instructions:call.request.instructions, references:[]});
      promptGroups.get(key).references.push(`${bundle.run.technology} / ${call.agent} / run ${bundle.run.id} / call ${call.id}`);
    }
  }
  const prompts = [...promptGroups.values()].map(group => `${group.references.join('\n')}\n${group.instructions}`);
  addNotes(s, "2'", `Non confrontare i soli caratteri del prompt come se fossero token di input. Le skill caricate entrano anche nei messaggi di contesto; gli specialisti A2A hanno richieste proprie.
Caratteri = lunghezza JavaScript UTF-16, non conteggio token. Tutte le varianti di lunghezza osservate sono mostrate, per gli agenti effettivamente invocati in uno o entrambi i giri.
ISTRUZIONI COMPLETE DELLE SEI RUN (testi identici deduplicati, con ogni run ID e call ID di riferimento; richieste logiche, non wire):
${prompts.join('\n\n------------------------------\n\n')}`);
}

// 16
{
  const s = pptx.addSlide();
  addHeader(s, 'Dai numeri alla diagnosi', 'Risposte, errori, agenti: gli esiti osservati',
    `${measurements.quality.successfulAssessments}/${measurements.quality.assessmentAttempts} valutazioni reso riuscite · ${measurements.quality.validationErrors} errori di validazione del motivo, non guasti di rete.`);
  measurements.architectureSummaries.forEach((summary, index) => {
    card(s, [0.7,4.8,8.9][index], 2.18, 3.75, 3.85, summary.title, summary.body, {
      kicker:'DUE RIPETIZIONI OSSERVATE', fill:[C.bluePale,C.pale,C.amberPale][index],
      line:[C.blue,C.teal,C.amber][index], accent:[C.blue,C.teal,C.amber][index], bodySize:14, titleSize:17,
    });
  });
  s.addText('Storico escluso: gli stop A2A per budget appartengono al precedente esperimento CON cap applicativi.', {
    x: 0.75, y: 6.2, w: 11.9, h: 0.38, fontSize: 12, bold: true, color: C.ink, margin: 0, fit: 'shrink',
  });
  addSource(s, 'Due ripetizioni non sono un benchmark. Risposte integrali, run ID, errori e valutazioni tool nelle note.');
  footer(s);
  addNotes(s, "2'", `CONCLUSIONI ATTUALI DERIVATE DAGLI EXPORT:
${measurements.architectureSummaries.map(summary => summary.conclusion).join('\n')}
${measurements.quality.caveat}
Non attribuire un eventuale errore di validazione a un guasto di rete. Distinguere risposta presente, verifica tool e correttezza dei fatti. La slide non certifica qualità solo perché una run è completed.
STORICO, NON ESITO CORRENTE: i precedenti A2A con budget 0,10 USD/run si fermavano prima della risposta. Il pilot a 1.500 output token/call e le sei run a 4.096/24 call/budget sono esclusi. Le directory originali restano intatte.
RISPOSTE INTEGRALI E RISULTATI DELLE SEI RUN:
${measuredRows.map(r => `${r.architecture.toUpperCase()} ${r.repetition} / ${r.runId} / ${r.status}\n${r.answer ?? `NESSUNA RISPOSTA FINALE: ${r.error}`}\nERRORE RUN: ${r.error ?? 'nessuno'}\nERRORI TOOL: ${JSON.stringify(r.errors)}\nERRORI MODELLO: ${JSON.stringify(r.modelErrors)}\nVALUTAZIONI RESO: ${JSON.stringify(r.assessments)}\nFONTI: ${JSON.stringify(r.sources)}`).join('\n\n')}`);
}

// 17
{
  const s = pptx.addSlide();
  addHeader(s, 'Pratiche operative', 'Sette modi per spendere meno senza “tagliare qualità”', 'Ottimizza il percorso, non soltanto il prezzo del modello.');
  const tips = [
    ['01', 'Recupera dati con tool mirati', 'non copiare il catalogo'],
    ['02', 'Compatta la history', 'ma conserva i fatti necessari'],
    ['03', 'Stabilizza il prefisso', 'e misura la cache'],
    ['04', 'Contratto di output', 'forma e dettaglio espliciti'],
    ['05', 'Limita call e retry', 'con errori visibili'],
    ['06', 'Route per task', 'Luna/Sol/Astra dopo eval'],
    ['07', 'Misura costo per esito', 'non solo per token'],
  ];
  tips.forEach((t, i) => {
    const col = i < 4 ? 0 : 1;
    const row = col === 0 ? i : i - 4;
    const x0 = col === 0 ? 0.75 : 6.75;
    const y0 = 2.08 + row * 1.1;
    s.addText(t[0], { x: x0, y: y0 + 0.08, w: 0.55, h: 0.3, fontSize: 16, bold: true, color: C.mint, align: 'center', margin: 0 });
    s.addShape(pptx.ShapeType.ellipse, { x: x0 - 0.02, y: y0, w: 0.58, h: 0.58, fill: { color: C.navy }, line: { color: C.navy }, transparency: 0 });
    s.addText(t[0], { x: x0 + 0.08, y: y0 + 0.2, w: 0.38, h: 0.16, fontSize: 9, bold: true, color: C.mint, align: 'center', margin: 0 });
    s.addText(t[1], { x: x0 + 0.8, y: y0, w: 4.9, h: 0.27, fontSize: 14, bold: true, color: C.ink, margin: 0 });
    s.addText(t[2], { x: x0 + 0.8, y: y0 + 0.39, w: 4.9, h: 0.22, fontSize: 11, color: C.muted, margin: 0 });
  });
  s.addShape(pptx.ShapeType.roundRect, { x: 6.75, y: 5.55, w: 5.8, h: 0.72, rectRadius: 0.06, fill: { color: C.pale }, line: { color: C.teal } });
  s.addText('Prima: correttezza e sicurezza. Poi: costo.', { x: 7.05, y: 5.79, w: 5.2, h: 0.24, fontSize: 15, bold: true, color: C.teal, align: 'center', margin: 0 });
  footer(s);
  addNotes(s, "1'", "Usare questa slide come sintesi rapida delle leve già spiegate. Routing per task significa usare eval e fallback, non mandare automaticamente tutto al modello più economico. Gli errori devono restare visibili.");
}

// 18
{
  const s = pptx.addSlide();
  s.background = { color: C.navy };
  s.addImage({ path: logo, x: 0.7, y: 0.58, w: 0.65, h: 0.65 });
  s.addText('IL CICLO', { x: 1.55, y: 0.76, w: 2.2, h: 0.22, fontSize: 10, bold: true, color: C.mint, charSpacing: 2, margin: 0 });
  s.addText('Osserva → ipotizza → cambia una variabile → verifica → conserva', { x: 0.78, y: 1.66, w: 11.75, h: 1.28, fontSize: 31, bold: true, color: C.white, align: 'center', valign: 'mid', margin: 0, fit: 'shrink' });
  const end = [
    ['QUALITÀ', 'prima'],
    ['LATENZA', 'scomposta'],
    ['COSTO', 'per esito'],
  ];
  end.forEach((e, i) => {
    const x = 2.05 + i * 3.2;
    s.addShape(pptx.ShapeType.roundRect, { x, y: 3.63, w: 2.7, h: 1.02, rectRadius: 0.08, fill: { color: i === 0 ? C.teal : '1E404D' }, line: { color: i === 0 ? C.mint : '345461' } });
    s.addText(e[0], { x, y: 3.9, w: 2.7, h: 0.2, fontSize: 12, bold: true, color: C.mint, align: 'center', charSpacing: 1, margin: 0 });
    s.addText(e[1], { x, y: 4.23, w: 2.7, h: 0.18, fontSize: 10.5, color: C.white, align: 'center', margin: 0 });
  });
  s.addText('Qual è la prima evidenza che manca nel vostro sistema?', { x: 1.55, y: 5.38, w: 10.2, h: 0.5, fontSize: 23, bold: true, color: C.mint, align: 'center', margin: 0 });
  s.addText('Domande', { x: 5.05, y: 6.35, w: 3.2, h: 0.34, fontSize: 18, bold: true, color: C.white, align: 'center', margin: 0 });
  addNotes(s, "5' Q&A · 00:55–01:00", "Chiudere chiedendo al pubblico quale costo o errore misurerebbe per primo. Riservare cinque minuti alle domande. Se la demo è andata lunga, usare soltanto la frase finale e la domanda.");
}

assert.equal(pptx._slides.length, 27, 'Exactly 27 slides required');
assert.equal(totalMinutes, 60, 'Speaker timings must total exactly 60 minutes');
pptx.writeFile({ fileName: path.join(__dirname, 'Osservare-AI-Andrea-Tosato-senza-limiti.pptx') })
  .then(() => console.log(`PPT saved: 27 slides, exactly 60 minutes; six validated unbounded runs from ${path.basename(measurementsDirectory)}.`))
  .catch(error => { console.error(error); process.exitCode = 1; });
