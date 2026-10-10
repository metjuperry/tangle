import { friendlyAiText } from '../api/aiText'

export type AiCall = { cost: number; reservedCost: number; status: string; error: string | null; createdAt: string; job: string | null;
  targets: string | null; outcome: string | null; prId: string | null; inputTokens: number; outputTokens: number; latencyMs: number }
export type AiStatus = { enabled: boolean; jevEnabled: boolean; calls: AiCall[] }

export function AiCalls({ calls }: { calls: AiCall[] | undefined }) {
  return <section className="evaluation-model" aria-label="AI call history">
    <details className="ai-history"><summary>Recent AI calls <span>{calls ? `(${calls.length})` : '(loading…)'}</span></summary>
      <p className="quiet">Request details and costs. Viewing this history makes no provider calls.</p>
      {calls?.length === 0 && <p className="quiet">No provider calls recorded.</p>}
      {calls?.map((call, i) => <article className="ai-call-row" key={i}>
        <div className="ai-call-heading"><b>{({ verdict: 'PR next-step assessment', pairs: 'Relationship assessment', evaluation: 'Evaluation replay', explanation: 'Written explanation' } as Record<string, string>)[call.job ?? ''] ?? 'Earlier AI call'}</b><span>{call.status}</span></div>
        {call.prId && <a href={`/?pr=${encodeURIComponent(call.prId)}`}>{call.prId} · View card</a>}
        <p>{call.error ? `Failed: ${call.error}` : call.outcome ? friendlyAiText(call.outcome) : (call.status === 'reserved' ? 'Waiting for completion; usage may be unknown.' : 'Details were not recorded for this earlier call.')}</p>
        {call.targets && <details><summary>What was assessed</summary><ul>{(JSON.parse(call.targets) as string[]).map(target => <li key={target}>{target.replaceAll('pr:', '')}</li>)}</ul></details>}
        <small>{new Date(call.createdAt).toLocaleString()} · ${call.cost.toFixed(6)}{call.status === 'reserved' && ` · $${call.reservedCost.toFixed(2)} budget reserved`} · {call.inputTokens} input / {call.outputTokens} output tokens · {call.latencyMs} ms</small>
      </article>)}
    </details>
  </section>
}
