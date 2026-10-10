import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'
import { AiCalls, type AiStatus } from './AiCalls'

type Stats = { decider: string; pairExamples: number; verdictExamples: number; precision: number | null; recall: number | null;
  agreement: number | null; pairBrier: number | null; verdictBrier: number | null; costPer100: number | null;
  latencyPer100Ms: number | null; disagreements: { target: string; expected: string; predicted: string }[] }
type Report = { labelledExamples: number; needed: number; excludedSnoozes: number; replayableExamples: number; stats: Stats[]; markdown: string }
const percent = (value: number | null) => value === null ? 'Not enough data' : `${(value * 100).toFixed(1)}%`
export function Evaluate() {
  const report = useQuery({ queryKey: ['evaluation'], queryFn: () => api<Report>('/api/evaluation') })
  const status = useQuery({ queryKey: ['ai'], queryFn: () => api<AiStatus>('/api/ai/status') })
  const [decider, setDecider] = useState('haiku-5-5')
  const [confirmed, setConfirmed] = useState(false)
  const client = useQueryClient()
  const replay = useMutation({ mutationFn: () => write<{ calls: number }>('/api/evaluation/replay', 'POST', { decider, confirmed }),
    onSuccess: async () => { setConfirmed(false); await client.invalidateQueries({ queryKey: ['evaluation'] }); await client.invalidateQueries({ queryKey: ['ai'] }) } })
  return <main className="settings-wrap"><section className="evaluation-page"><a href="/settings">Back to Settings</a><h1>Evaluate</h1>
    <p>How well do the suggestions match your decisions? Viewing stats makes no AI calls.</p>
    {report.isPending && <p role="status">Reading your labelled decisions…</p>}
    {report.error && <p role="alert">{report.error.message}</p>}
    {report.data && <><p className="evaluation-coverage"><b>{report.data.labelledExamples}</b> labelled examples
      {report.data.needed > 0 ? ` · ${report.data.needed} more needed for a useful 30-example report.` : ' · Enough for an initial report.'}</p>
      <p className="quiet">{report.data.excludedSnoozes} Snooze choices excluded; {report.data.replayableExamples} examples have original inputs for replay. Metrics cover your labelled set, not every possible relationship.</p>
      {report.data.stats.length === 0 && <p>No scored examples yet. Decide suggested strings and Untangle cards to build the answer key.</p>}
      {report.data.stats.map(stats => <section className="evaluation-model" key={stats.decider}><h2>{stats.decider === 'jev' ? 'Jev' : stats.decider === 'haiku-5-5' ? 'Haiku 5.5' : stats.decider}</h2>
        <p>{stats.pairExamples} relationship examples · {stats.verdictExamples} next-step examples</p>
        <dl className="evaluation-metrics">
          <div><dt>String precision</dt><dd>{percent(stats.precision)}</dd></div><div><dt>String recall</dt><dd>{percent(stats.recall)}</dd></div>
          <div><dt>Next-step agreement</dt><dd>{percent(stats.agreement)}</dd></div>
          <div><dt>Relationship Brier</dt><dd>{stats.pairBrier?.toFixed(3) ?? 'Not enough data'}</dd></div>
          <div><dt>Next-step Brier</dt><dd>{stats.verdictBrier?.toFixed(3) ?? 'Not enough data'}</dd></div>
          <div><dt>Cost / 100 calls</dt><dd>{stats.costPer100 === null ? 'Not enough data' : `$${stats.costPer100.toFixed(4)}`}</dd></div>
          <div><dt>Latency / 100 calls</dt><dd>{stats.latencyPer100Ms === null ? 'Not enough data' : `${(stats.latencyPer100Ms / 1000).toFixed(1)} s`}</dd></div>
        </dl><p className="quiet">Lower Brier is better. Next-step calibration scores confidence in the chosen action, not a full multiclass distribution.</p>
        <details><summary>Disagreements ({stats.disagreements.length})</summary>{stats.disagreements.map((item, i) =>
          <p key={i}>{item.target.replaceAll('pr:', '').replace('\n', ' ↔ ')} · You: {item.expected} · AI: {item.predicted}</p>)}</details>
      </section>)}
      <a href="/api/evaluation/report" download="tangle-evaluation.md">Download Markdown report</a>
    </>}
    <section className="evaluation-replay"><h2>Compare with a fresh replay</h2><p>This sends original inputs from currently allowed repos to the selected provider and may cost money. It does not change card verdicts or strings.</p>
      <label>Evaluation decider<select value={decider} onChange={event => { setDecider(event.target.value); setConfirmed(false) }}>
        <option value="haiku-5-5">Haiku 5.5</option><option value="jev" disabled={!status.data?.jevEnabled}>Jev {!status.data?.jevEnabled && '(disabled)'}</option></select></label>
      <label className="replay-consent"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I authorize provider calls within my monthly budget.</label>
      <button disabled={!confirmed || !status.data?.enabled || replay.isPending || !report.data?.replayableExamples} onClick={() => replay.mutate()}>{replay.isPending ? 'Evaluating…' : 'Run evaluation replay'}</button>
      {!status.data?.enabled && <p className="quiet">Provider calls are disabled on the server.</p>}
      {status.error && <p role="alert">{status.error.message}</p>}{replay.error && <p role="alert">{replay.error.message}</p>}
      {replay.data && <p role="status">Replay finished: {replay.data.calls} new calls. Previously replayed examples were skipped.</p>}
    </section><AiCalls calls={status.data?.calls} /></section></main>
}
