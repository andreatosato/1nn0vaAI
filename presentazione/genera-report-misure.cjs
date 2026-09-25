const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const directory = path.resolve(__dirname, process.argv[2] || 'misurazioni-2026-09-25-unbounded');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const manifest = read(path.join(directory, 'experiment.json'));
const escape = value => String(value ?? 'N/D').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number = (value, digits = 0) => value == null ? 'N/D' : value.toLocaleString('it-IT', { maximumFractionDigits: digits });
const sum = (calls, key) => calls.every(call => Number.isFinite(call[key])) ? calls.reduce((total, call) => total + call[key], 0) : null;
const csv = rows => rows.map(row => row.map(value => `"${String(value ?? '').replaceAll('"', '""')}"`).join(';')).join('\r\n');
const write = (file, content) => fs.writeFileSync(path.join(directory, file), content, 'utf8');
const rows = [];
const bundles = [];
const callsTable = [['architecture','repetition','run_id','call_id','agent','model','input','cached','cache_write','output','reasoning','duration_ms','cost_usd','finish_reason','prompt_characters','usage_source','actual_max_output_tokens']];
let catalogHash;

assert.equal(manifest.configuration.unboundedExecution, true, 'This report requires unbounded execution');
assert.equal(manifest.configuration.approvedBudgetUsd, null, 'No application budget may be active');
assert.equal(manifest.configuration.mode, 'live');
assert.equal(manifest.configuration.modelProfileId, 'gpt5');
assert.equal(manifest.configuration.promptProfile, 'good');
assert.equal(manifest.configuration.historyStrategy, 'full');
assert.equal(manifest.configuration.toolTransport, 'direct');
assert.equal(manifest.configuration.confirmAction, false);
assert.deepEqual(manifest.configuration.agentModels, {});
assert.ok(Object.values(manifest.configuration.promptBlocks).every(value => value === false));
assert.deepEqual(manifest.executionOrder.map(({technology, repetition}) => `${technology}-${repetition}`).sort(),
  ['a2a-1','a2a-2','inline-1','inline-2','skills-1','skills-2']);

for (const {technology, repetition} of manifest.executionOrder) {
  const stem = `${technology}-${repetition}`;
  const bundle = read(path.join(directory, `${stem}-export.json`));
  bundles.push(bundle);
  const run = bundle.run;
  const calls = bundle.ledger.calls;
  assert.ok(['completed','failed'].includes(run.status), `${stem}: nonterminal run`);
  if (run.status === 'completed') assert.ok(run.result?.answer?.trim(), `${stem}: empty answer`);
  assert.equal(run.technology, technology);
  assert.equal(run.message, manifest.question);
  assert.deepEqual(run.configuration, manifest.configuration, `${stem}: configuration mismatch`);
  assert.deepEqual(bundle.provenance.request.configuration, run.configuration);
  assert.equal(bundle.provenance.request.message, manifest.question);
  assert.ok(bundle.sanitized, `${stem}: export must be sanitized`);
  assert.ok(calls.length > 0, `${stem}: no provider calls`);
  assert.equal(new Set(calls.map(call => call.id)).size, calls.length);
  for (const call of calls) {
    assert.equal(call.runId, run.id);
    assert.equal(call.usageSource, 'provider');
    assert.equal(call.mode, 'live');
    assert.equal(call.modelProfileId, manifest.configuration.modelProfileId);
    assert.ok(call.request?.instructions, `${stem}/${call.id}: instructions missing`);
    assert.equal(call.request.parameters?.maxOutputTokens, null, `${stem}/${call.id}: actual model output cap must be null`);
    for (const key of ['inputTokens','outputTokens','cachedInputTokens','durationMs','estimatedCostUsd']) {
      assert.ok(Number.isFinite(call[key]) && call[key] >= 0, `${stem}/${call.id}: invalid ${key}`);
    }
    assert.ok(call.cachedInputTokens <= call.inputTokens);
    assert.ok(call.reasoningTokens == null || (call.reasoningTokens >= 0 && call.reasoningTokens <= call.outputTokens));
  }
  const hash = bundle.provenance.catalog.contentHash;
  assert.ok(hash);
  catalogHash ??= hash;
  assert.equal(hash, catalogHash, `${stem}: different catalog`);
  assert.equal(sum(calls, 'inputTokens'), run.inputTokens);
  assert.equal(sum(calls, 'outputTokens'), run.outputTokens);
  const cost = sum(calls, 'estimatedCostUsd');
  assert.ok(cost != null && Math.abs(cost - run.estimatedCostUsd) < 1e-9, `${stem}: cost total mismatch`);
  assert.ok(Number.isFinite(run.durationMs) && run.durationMs >= 0);
  const agents = [...new Set(calls.map(call => call.agent))];
  const promptCharacters = Object.fromEntries(agents.map(agent => [
    agent, [...new Set(calls.filter(call => call.agent === agent).map(call => call.request.instructions.length))],
  ]));
  const toolEvents = bundle.timeline.filter(event => event.kind === 'tool.called');
  const failures = toolEvents.filter(event => event.data?.status === 'failed');
  rows.push({
    architecture: technology, repetition, status: run.status, error: run.error, runId: run.id, conversationId: run.conversationId,
    calls: calls.length, agents, input: run.inputTokens, cached: sum(calls, 'cachedInputTokens'),
    cacheWrite: sum(calls, 'cacheWriteTokens'), output: run.outputTokens, reasoning: sum(calls, 'reasoningTokens'),
    durationMs: run.durationMs, costUsd: run.estimatedCostUsd, promptCharacters,
    toolFailures: failures.length, errors: failures.map(event => ({agent:event.agent, message:event.message, data:event.data})),
    modelErrors: calls.filter(call => call.error || call.status === 'failed').map(call => ({callId:call.id, agent:call.agent, error:call.error, status:call.status})),
    assessments: toolEvents.filter(event => event.message === 'assess_return' || event.data?.name === 'assess_return')
      .map(event => ({agent:event.agent, ...event.data})),
    tools: [...new Set(toolEvents.map(event => event.data?.name ?? event.message))],
    answer: run.result?.answer ?? null, sources: run.result?.sources ?? [], exportFile: `${stem}-export.json`,
  });
  for (const call of calls) {
    callsTable.push([technology,repetition,run.id,call.id,call.agent,call.modelId,call.inputTokens,call.cachedInputTokens,
      call.cacheWriteTokens,call.outputTokens,call.reasoningTokens,call.durationMs,call.estimatedCostUsd,
      call.response?.finishReason,call.request.instructions.length,call.usageSource,call.request.parameters.maxOutputTokens]);
  }
}

assert.equal(rows.length, 6);
assert.equal(new Set(rows.map(row => row.runId)).size, 6, 'Six distinct runs required');
assert.equal(new Set(rows.map(row => row.conversationId)).size, 6, 'New conversation required for each run');
assert.ok(rows.every(row => row.conversationId));
const total = Number(rows.reduce((a, r) => a + r.costUsd, 0).toFixed(10));
const label = architecture => ({inline:'Inline',skills:'Skills',a2a:'A2A'})[architecture];
const architectureSummaries = ['inline','skills','a2a'].map(architecture => {
  const runs = rows.filter(row => row.architecture === architecture).sort((a,b) => a.repetition - b.repetition);
  const completed = runs.filter(row => row.status === 'completed').length;
  const answers = runs.filter(row => row.answer).length;
  const agents = [...new Set(runs.flatMap(row => row.agents))];
  const assessmentAttempts = runs.flatMap(row => row.assessments);
  const successfulAssessments = assessmentAttempts.filter(attempt => attempt.status === 'completed').length;
  return {
    architecture, title: `${label(architecture)}: ${completed}/2 completate`,
    body: runs.map(row => `Prova ${row.repetition}: ${row.calls} call, ${row.toolFailures} errori tool, ${row.answer ? 'risposta presente' : 'nessuna risposta'}.`).join('\n\n') +
      `\n\nReso: ${successfulAssessments}/${assessmentAttempts.length} verifiche tool riuscite.\nAgenti: ${agents.join(', ')}.`,
    conclusion: `${label(architecture)}: ${completed}/2 completed; ${answers}/2 risposte. Call: ${runs.map(row => row.calls).join(' / ')}. Errori tool: ${runs.map(row => row.toolFailures).join(' / ')}. Valutazioni reso riuscite: ${successfulAssessments}/${assessmentAttempts.length}. Agenti osservati: ${agents.join(', ')}.`,
  };
});
const allAssessments = rows.flatMap(row => row.assessments);
const quality = {
  assessmentAttempts: allAssessments.length,
  successfulAssessments: allAssessments.filter(attempt => attempt.status === 'completed').length,
  validationErrors: allAssessments.filter(attempt => attempt.status === 'failed' &&
    attempt.error === 'Motivo valido: defect, change-of-mind oppure unknown.').length,
};
quality.caveat = `${quality.successfulAssessments}/${quality.assessmentAttempts} valutazioni reso riuscite; ${quality.validationErrors} errori di validazione del motivo. Risposta presente non significa verifica tool riuscita.`;
const protocol = {
  applicationCaps: 'Nessun cap applicativo su output, numero di chiamate o budget. Restano i limiti tecnici del provider/SDK.',
  actualMaxOutputTokens: null,
  compatibilityFields: { maxOutputTokens: manifest.configuration.maxOutputTokens, maxModelCalls: manifest.configuration.maxModelCalls },
  compatibilityNotice: 'maxOutputTokens e maxModelCalls nella configurazione sono campi di compatibilità IGNORATI con unboundedExecution=true; il parametro effettivo del modello maxOutputTokens è null in ogni call.',
  order: manifest.executionOrder.map(({technology,repetition}) => `${label(technology)} ${repetition}`).join(' → '),
};
const assertions = {
  sixDistinctRunsAndConversations: true, twoRepeatsPerArchitecture: true, identicalQuestionAndConfiguration: true,
  unboundedExecution: true, approvedBudgetUsd: null, allActualMaxOutputTokensNull: true,
  liveProviderUsage: true, identicalCatalog: true, tokenAndCostTotalsMatch: true,
};
const result = {experiment:manifest, catalogHash, protocol, assertions, quality, totalControlledCostUsd:total, architectureSummaries, rows};

// Write only after every export has passed validation; never touch the historical experiment.
for (const [i, row] of rows.entries()) {
  const stem = `${row.architecture}-${row.repetition}`;
  const calls = bundles[i].ledger.calls;
  write(`${stem}-risposta.txt`, row.answer ?? `NESSUNA RISPOSTA FINALE. Run ${row.status}: ${row.error}`);
  write(`${stem}-prompt.txt`, calls.map((call, index) =>
    `RUN ${row.runId} | CALL ${index + 1} | ${call.agent} | ${call.id}\n${'='.repeat(72)}\n${call.request.instructions}`).join('\n\n'));
  write(`${stem}-richieste.json`, JSON.stringify(calls.map(call => ({
    callId:call.id, agent:call.agent, captureKind:call.captureKind, request:call.request,
  })), null, 2));
}
write('risultati.json', JSON.stringify(result, null, 2));
write('riepilogo.csv', '\uFEFF' + csv([
  ['architecture','repetition','status','error','run_id','calls','agents','input','cached','cache_write','output','reasoning','duration_ms','cost_usd','failed_tool_calls','prompt_characters_by_agent'],
  ...rows.map(r => [r.architecture,r.repetition,r.status,r.error,r.runId,r.calls,r.agents.join(', '),r.input,r.cached,r.cacheWrite,r.output,r.reasoning,r.durationMs,r.costUsd,r.toolFailures,JSON.stringify(r.promptCharacters)]),
]));
write('chiamate.csv', '\uFEFF' + csv(callsTable));
const headers = ['Architettura','Prova','Stato','Call','Input','Cached','Output','Reasoning','Secondi','USD','Tool falliti'];
const table = `<table><thead><tr>${headers.map(h=>`<th>${h}</th>`).join('')}</tr></thead><tbody>${rows.map(r=>`<tr>${[
  r.architecture,r.repetition,r.status,r.calls,number(r.input),number(r.cached),number(r.output),number(r.reasoning),
  number(r.durationMs/1000,2),number(r.costUsd,8),r.toolFailures,
].map(v=>`<td>${escape(v)}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
write('report.html', `<!doctype html><html lang="it"><meta charset="utf-8"><title>Sei run senza cap applicativi</title>
<style>body{font:16px/1.55 "Segoe UI",sans-serif;color:#173342;margin:32px}table{border-collapse:collapse}th,td{padding:9px;border:1px solid #d6e0e4}pre{white-space:pre-wrap;overflow-wrap:anywhere}section{margin:32px 0}a{margin-right:18px}</style>
<h1>Una domanda, tre architetture, due ripetizioni LIVE</h1>
<h2>Protocollo</h2><blockquote>${escape(manifest.question)}</blockquote>
<p>GPT-5 / GOOD · chat nuova per run · full history · trasporto direct · nessun override modello · blocchi opzionali spenti · nessuna bozza autorizzata.</p>
<p>${escape(protocol.applicationCaps)} ${escape(protocol.compatibilityNotice)}</p>
<p>unboundedExecution=true; approvedBudgetUsd=null; parametro effettivo maxOutputTokens=null.</p>
<p>Ordine sequenziale: ${escape(protocol.order)}. Cache non azzerata né controllata: non chiamare i giri cold/warm.</p>
<p>Catalogo identico: ${escape(catalogHash)}. Dati commerciali sintetici. GOOD indica il profilo, non istruzioni identiche fra architetture.</p>
${table}<p>Totale delle sole sei run correnti: ${number(total,8)} USD. Stime ledger, non fattura.</p>
<h2>Conclusioni descrittive</h2><ul>${architectureSummaries.map(s=>`<li>${escape(s.conclusion)}</li>`).join('')}</ul>
<p><strong>${escape(quality.caveat)}</strong> Un motivo non valido è un errore di argomento, non prova che il servizio sia indisponibile. Leggere le risposte integrali per distinguere conferme, riserve e diagnosi non supportate.</p>
<p>Risposta presente e completed non certificano la correttezza: valutare fatti, policy, fonti, errori e recuperi. Due ripetizioni non sono un benchmark né una graduatoria causale.</p>
<h2>Semantica</h2><p>Input include cached; output include reasoning e output intermedi/tool call. Non sommare due volte i sottocontatori.
Caratteri delle istruzioni (unità UTF-16 di JavaScript) NON sono token. Input comprende anche messaggi, tool e risultati.
Durata end-to-end, non TTFT; non sommare le latenze parallele A2A. N/D non significa zero. Errori tool: eventi tool.called con status failed, senza duplicare gli eventi HTTP.</p>
<h2>Esperimenti precedenti, esclusi</h2><p>I precedenti esperimenti con cap (pilot a 1.500 e sei run a 4.096 token/call, 24 call, budget 0,10 USD/run) sono storici, non queste misure.
I precedenti stop A2A per budget non descrivono l’esito corrente. Directory ed export storici restano invariati; i loro costi non entrano nel totale qui mostrato.</p>
<a href="risultati.json">Risultati e verifiche</a><a href="riepilogo.csv">CSV run</a><a href="chiamate.csv">CSV call</a>
${rows.map(r=>`<section><h2>${label(r.architecture)} ${r.repetition} · ${escape(r.status)}</h2>
<p>Run ${escape(r.runId)} · ${escape(r.error ?? '')}</p><p>Agenti: ${escape(r.agents.join(', '))}. Tool: ${escape(r.tools.join(', '))}.</p>
<p>Caratteri per agente: ${escape(JSON.stringify(r.promptCharacters))}. Cache write: ${number(r.cacheWrite)}.</p>
<a href="${r.exportFile}">Export</a><a href="${r.architecture}-${r.repetition}-prompt.txt">Istruzioni complete per call</a>
<a href="${r.architecture}-${r.repetition}-richieste.json">Richieste logiche (non wire)</a>
<h3>Risposta integrale</h3><pre>${escape(r.answer ?? 'NESSUNA RISPOSTA FINALE')}</pre>
<h3>Errori tool e modello</h3><pre>${escape(JSON.stringify({tool:r.errors,model:r.modelErrors},null,2))}</pre>
<h3>Valutazioni reso: argomenti ed esiti reali</h3><pre>${escape(JSON.stringify(r.assessments,null,2))}</pre></section>`).join('\n')}</html>`);
console.log(headers.join(' | '));
for (const r of rows) console.log([r.architecture,r.repetition,r.status,r.calls,r.input,r.cached,r.output,r.reasoning,(r.durationMs/1000).toFixed(2),r.costUsd,r.toolFailures].join(' | '));
console.log(`Verified six unbounded LIVE/provider exports, actual maxOutputTokens=null, distinct conversations, configuration/catalog and totals. Total USD=${total}`);
