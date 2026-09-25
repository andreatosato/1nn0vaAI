const pptxgen = require('pptxgenjs');
const path = require('path');
const fs = require('node:fs');
const assert = require('node:assert/strict');

const measurementsDirectory = path.resolve(__dirname, process.argv[2] || 'misurazioni-2026-09-25-unbounded');
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

function addHeader(slide, section, title, subtitle) {
  slide.background = { color: C.bg };
  slide.addText(section.toUpperCase(), { x: 0.65, y: 0.38, w: 8.2, h: 0.25, fontSize: 10, bold: true, color: C.teal, charSpacing: 2.2, margin: 0 });
  slide.addText(title, { x: 0.65, y: 0.76, w: 11.95, h: 0.64, fontSize: 27, bold: true, color: C.ink, margin: 0, breakLine: false, fit: 'shrink' });
  if (subtitle) slide.addText(subtitle, { x: 0.68, y: 1.43, w: 11.8, h: 0.4, fontSize: 13, color: C.muted, margin: 0, fit: 'shrink' });
  slide.addShape(pptx.ShapeType.line, { x: 0.65, y: 1.92, w: 12.0, h: 0, line: { color: C.line, width: 1 } });
}

function footer(slide, n, timing) {
  slide.addText(`${String(n).padStart(2, '0')}  ·  AI OBSERVATORY`, { x: 0.65, y: 7.08, w: 3, h: 0.2, fontSize: 8.5, bold: true, color: C.muted, charSpacing: 1, margin: 0 });
  if (timing) slide.addText(timing, { x: 10.5, y: 7.05, w: 2.15, h: 0.22, fontSize: 9, color: C.teal, bold: true, align: 'right', margin: 0 });
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
  totalMinutes += Number.parseInt(minutes, 10);
  slide.addNotes(`[TEMPO: ${minutes}]\n${notes}`);
}

function addSource(slide, text) {
  slide.addText(text, { x: 0.68, y: 6.72, w: 11.9, h: 0.2, fontSize: 7.5, color: '718690', italic: true, margin: 0, fit: 'shrink' });
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
  addNotes(s, "2'", "Aprire con la domanda: una risposta fluida è davvero una buona risposta? Promessa: seguiremo una richiesta dall'utente al ledger. Dichiarare che scenario e dati sono sintetici, il catalogo è uno snapshot pubblico, mentre la telemetria LIVE è reale quando esplicitamente indicato.");
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
    s.addShape(pptx.ShapeType.ellipse, { x: xs[i] + 0.78, y: 2.28, w: 1.08, h: 1.08, fill: { color: it[4] }, line: { color: it[2], width: 2 } });
    s.addText(String(i + 1), { x: xs[i] + 1.03, y: 2.57, w: 0.58, h: 0.36, fontSize: 22, bold: true, color: it[2], align: 'center', margin: 0 });
    s.addText(it[0], { x: xs[i], y: 3.62, w: 2.65, h: 0.28, fontSize: 12, bold: true, color: it[2], align: 'center', charSpacing: 1.2, margin: 0 });
    s.addText(it[1], { x: xs[i], y: 4.12, w: 2.65, h: 1.05, fontSize: 15, bold: true, color: C.ink, align: 'center', valign: 'mid', margin: 0, breakLine: false, fit: 'shrink' });
  });
  s.addText('Ottimizzare una sola metrica può peggiorare il sistema.', { x: 2.55, y: 5.73, w: 8.2, h: 0.46, fontSize: 22, bold: true, color: C.navy, align: 'center', margin: 0 });
  footer(s, 2, "00:02–00:04");
  addNotes(s, "2'", "Coinvolgere il pubblico: quale errore è più grave, tono poco elegante, prezzo inventato o reso autorizzato per il motivo sbagliato? La risposta introduce la necessità di misurare qualità e non solo velocità/costo.");
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
  footer(s, 3, "00:04–00:07");
  addNotes(s, "3'", "Spiegare gli expected facts prima di guardare l'output. Una risposta può essere persuasiva e sbagliata. La rubrica permette di ripetere il confronto e separa giudizio qualitativo da telemetria quantitativa.");
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
  footer(s, 4, "00:07–00:10");
  addNotes(s, "3'", "Mostrare i tre percorsi: Inline e Skills hanno un solo agente modello Router; A2A delega agli specialisti remoti. I servizi business sono distinti dagli agenti. Il confronto tra architetture non isola automaticamente il solo overhead di rete.");
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
  footer(s, 5, "00:10–00:14");
  addNotes(s, "4'", "Messaggio: non esiste il prompt universalmente ottimale. Per 5.4 evidenziare la gestione esplicita della verbosità; per 5.6 partire da risultato/prove/vincoli; per GPT-6 governare autonomia, chiarimenti, skill e priorità. Non confondere istruzioni di verbosità con reasoning effort.");
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
  footer(s, 6, "00:14–00:18");
  addNotes(s, "4'", "Astra: più capace per lavori difficili; Sol: complessi workflow agentici/coding; Luna: efficienza per task focalizzati e volume. Terra: la richiesta lo cita, ma la model card ufficiale non è disponibile: non inventare differenze. Nella demo LIVE sono verificati GPT-5 e Sol; Astra/Luna non sono ancora live-ready. Le tariffe mostrate sono orientative del listino OpenAI, non la fattura Azure.");
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
  footer(s, 7, "00:18–00:21");
  addNotes(s, "3'", "Usare questa slide come checklist di prompt review. Un prompt lungo non è necessariamente cattivo; un prompt breve non è necessariamente economico se genera chiamate inutili, chiarimenti o output errati.");
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
  footer(s, 8, "00:21–00:25");
  addNotes(s, "4'", "Spiegare i bucket senza doppio conteggio. Input totale può includere cached e cache write; reasoning è incluso nell'output. Un valore assente non vale zero. La semantica del provider e della rate card deve essere congelata nella run.");
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
  footer(s, 9, "00:25–00:28");
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
  footer(s, 10, "00:28–00:31");
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
  footer(s, 11, "00:31–00:34");
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
  footer(s, 12, "00:34–00:37");
  addNotes(s, "3'", "Spiegare che A/C non è un confronto a una variabile. Usare chat nuove per ogni variante. Alternare ordine per ridurre bias temporali. Una singola risposta non crea una classifica.");
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
  footer(s, 13, "00:37–00:47");
  addNotes(s, "10'", `DEMO. Mostrare dallo Storico le sei run già persistite e le loro tracce, senza doverle rieseguire tutte sul palco. Se si esegue una nuova prova, usare la domanda e la configurazione esatte; consenso manuale prima dell'invio. Il percorso misurato è un solo turno per chat, non la conversazione multi-turn originaria.
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
  addHeader(s, 'Misure LIVE · 25 settembre 2026', 'Sei run reali: token, latenza, costo ed esito', 'GPT-5 / GOOD · stessa domanda · nessun cap applicativo su output, call o budget');
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
  addSource(s, 'Fonte: sei export LIVE/provider del 25/09/2026. Run ID e contatori non arrotondati nelle note. Stime ledger, non fattura.');
  footer(s, 14, "00:47–00:50");
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
  footer(s, 15, "00:50–00:52");
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
  footer(s, 16, "00:52–00:54");
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
  footer(s, 17, "00:54–00:55");
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

assert.equal(pptx._slides.length, 18, 'Exactly 18 slides required');
assert.equal(totalMinutes, 60, 'Speaker timings must total exactly 60 minutes');
pptx.writeFile({ fileName: path.join(__dirname, 'Osservare-AI-Andrea-Tosato-senza-limiti.pptx') })
  .then(() => console.log('PPT saved: 18 slides, exactly 60 minutes; six validated unbounded runs.'))
  .catch(error => { console.error(error); process.exitCode = 1; });
