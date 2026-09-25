import type { ModelCallRecord, RunRecord } from '../contracts';
import { money, tokens } from '../lib/format';
import { summarizeAgentUsage, summarizeConversationUsage } from '../lib/usage';
import { EmptyState, ErrorBox, SectionHeading } from './Common';

export function UsageView({ run, calls, runs, conversationId, runsError }: {
  run: RunRecord | null;
  calls: readonly ModelCallRecord[];
  runs: readonly RunRecord[] | null;
  conversationId: string | null;
  runsError: string | null;
}) {
  const activeConversationId = conversationId ?? run?.conversationId ?? null;
  const conversationRuns = new Map(
    (runs ?? []).filter((record) => record.conversationId === activeConversationId).map((record) => [record.id, record]),
  );
  if (run?.conversationId === activeConversationId) conversationRuns.set(run.id, run);
  const conversationCalls = new Map<string, ModelCallRecord>();
  for (const record of conversationRuns.values()) {
    for (const call of record.calls) conversationCalls.set(call.id, call);
  }
  if (run?.conversationId === activeConversationId) {
    for (const call of calls) {
      if (call.runId === run.id) conversationCalls.set(call.id, call);
    }
  }
  const allCalls = runsError || runs === null ? [] : [...conversationCalls.values()];
  const agents = summarizeAgentUsage(allCalls);
  const totals = summarizeConversationUsage(allCalls);
  const totalsReady = !runsError && runs !== null;
  return <section className="panel view-panel">
    <SectionHeading title="Token e costi" />
    {runsError && <ErrorBox message={runsError} title="Impossibile leggere i run della conversazione" />}
    {!activeConversationId ? <EmptyState title="Nessuna conversazione selezionata" icon="chart">Apri una conversazione dallo storico.</EmptyState> : <>
      <div className="metrics-grid usage-summary">
        <div className="metric"><span>Conversazione · token</span><strong>{tokens(totalsReady ? totals.totalTokens : null)}</strong></div>
        <div className="metric"><span>Conversazione · prezzo</span><strong>{money(totalsReady ? totals.estimatedCostUsd : null)}</strong></div>
      </div>
      <div className="table-scroll" tabIndex={0} aria-label="Riepilogo token e prezzo per agente">
        <table><caption>Riepilogo token e prezzo per agente</caption>
          <thead><tr><th scope="col">Agente</th><th scope="col">Token</th><th scope="col">Prezzo</th></tr></thead>
          <tbody>{agents.map((agent) => <tr key={agent.key}>
            <th scope="row">{agent.agent}</th>
            <td>{tokens(totalsReady ? agent.totalTokens : null)}</td>
            <td>{money(totalsReady ? agent.estimatedCostUsd : null)}</td>
          </tr>)}
          {agents.length === 0 && <tr><td colSpan={3}>Nessun dato</td></tr>}
          </tbody>
        </table>
      </div>
    </>}
  </section>;
}
