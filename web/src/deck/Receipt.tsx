import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'
import { actionResultLabel, actionResultUrl } from './actionResult'
import type { Decision } from './Untangle'
import { useReducedMotion } from 'motion/react'

export type Action = { id: string; prId: string; kind: string; payload: string; status: string; result: string | null }
type SavedRound = { id: string; createdAt: string; actions: Action[]; decisions: Decision[] }
function ActionResult({ action }: { action: Action }) {
  return <div className={`receipt-item receipt-result ${action.status === 'failed' ? 'receipt-failed' : ''}`}>
    <b>{actionResultLabel(action.kind, action.status)}</b><span>{action.prId}</span>
    {action.status === 'failed' && <p>{action.result ?? 'No result recorded. Check GitHub before retrying.'}</p>}
    <a href={actionResultUrl(action.prId, action.kind, action.result)} target="_blank" rel="noreferrer">View on GitHub</a>
  </div>
}
export function ActionComposer({ prId, onClose, onQueued, initialKind = 'comment', onLocalDecision, allowStillMyTurn = false }: { prId: string; onClose: () => void; onQueued?: (id: string) => void; initialKind?: string; onLocalDecision?: () => Promise<unknown>; allowStillMyTurn?: boolean }) {
  const [kind, setKind] = useState(initialKind)
  const [text, setText] = useState('')
  const [reviewer, setReviewer] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  const client = useQueryClient()
  useEffect(() => { dialog.current?.showModal() }, [])
  const save = useMutation({ mutationFn: () => write<Action>('/api/actions', 'POST', { prId, kind,
    payload: kind === 'comment' ? { text } : kind === 'request_review' ? { reviewer } : {} }),
    onSuccess: async action => { await client.invalidateQueries({ queryKey: ['actions'] }); onQueued?.(action.id); onClose() } })
  const local = useMutation({
    mutationFn: async () => {
      if (!onLocalDecision) throw new Error('Local decision is unavailable.')
      await onLocalDecision()
    },
    onSuccess: onClose,
  })
  const busy = save.isPending || local.isPending
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="queue-heading"
    onCancel={event => { if (busy) event.preventDefault(); else onClose() }}>
    <h2 id="queue-heading">{allowStillMyTurn ? 'Fix' : 'Queue action'} · {prId}</h2>
    <p>{kind === 'still_my_turn' ? 'Acknowledge your work and move to the next card. No GitHub action is queued; the computed turn stays unchanged.' : 'Nothing is posted until you confirm Apply on the receipt.'}</p>
    <form onSubmit={e => { e.preventDefault(); if (kind === 'still_my_turn') local.mutate(); else save.mutate() }}>
      <label>Action kind<select autoFocus disabled={busy} value={kind} onChange={e => { setKind(e.target.value); save.reset(); local.reset() }}>
        {allowStillMyTurn && <option value="still_my_turn">Still my turn</option>}
        <option value="comment">Post comment</option><option value="request_review">Request review</option><option value="close">Close PR</option><option value="merge">Merge PR</option></select></label>
      {kind === 'comment' && <label>Comment text<textarea value={text} onChange={e => setText(e.target.value)} required /></label>}
      {kind === 'request_review' && <label>Reviewer login<input value={reviewer} onChange={e => setReviewer(e.target.value)} required /></label>}
      {kind === 'merge' && <p>Tangle captures the current PR version from GitHub when you queue this merge. If new commits arrive before Apply, you’ll need to queue it again.</p>}
      {kind === 'close' && <p>Closes this PR without posting a comment. Queue a separate comment if needed.</p>}
      <button disabled={busy}>{kind === 'still_my_turn' ? (local.isPending ? 'Saving…' : 'Record decision') : save.isPending && kind === 'merge' ? 'Checking PR version…' : 'Queue action'}</button><button type="button" disabled={busy} onClick={onClose}>Cancel</button>
      {onLocalDecision && kind !== 'still_my_turn' && <button type="button" disabled={busy} onClick={() => local.mutate()}>Record decision without GitHub action</button>}
      {(save.error || local.error) && <p role="alert">{save.error?.message ?? local.error?.message}</p>}
    </form></dialog>
}
function Confirmation({ action, onClose }: { action: Action; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const client = useQueryClient()
  const payload: { Text?: string; Reviewer?: string; HeadSha?: string } = JSON.parse(action.payload)
  useEffect(() => { dialog.current?.showModal() }, [])
  const apply = useMutation({ mutationFn: () => write(`/api/actions/${action.id}/apply`, 'POST', { confirmed: true }),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['actions'] }); onClose() },
    onError: () => { void client.invalidateQueries({ queryKey: ['actions'] }) } })
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="apply-heading" onCancel={onClose}>
    <h2 id="apply-heading">Confirm GitHub action</h2><p><b>{action.prId}</b> · {action.kind}</p>
    {action.kind === 'comment' && <pre className="confirmation-text">{payload.Text}</pre>}
    {action.kind === 'request_review' && <p>Request review from <b>{payload.Reviewer}</b>.</p>}
    {action.kind === 'merge' && <><p>Merge the PR version captured when this action was queued, using a merge commit. New commits will block this action.</p>
      <details><summary>Captured version</summary><code>{payload.HeadSha}</code></details></>}
    {action.kind === 'close' && <p>Close the PR without a comment.</p>}
    <button disabled={apply.isPending} onClick={() => apply.mutate()}>Confirm and apply</button><button disabled={apply.isPending} onClick={onClose}>Cancel</button>
    {apply.error && <p role="alert">{apply.error.message}</p>}</dialog>
}
function TearConfirmation({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])
  const save = useMutation({ mutationFn: () => write('/api/receipts/tear', 'POST', { confirmed: true }),
    onSuccess: () => { onSaved(); onClose() } })
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="tear-heading"
    onCancel={event => { if (save.isPending) event.preventDefault(); else onClose() }}>
    <h2 id="tear-heading">Tear and save this round?</h2>
    <p>Applied actions and your decisions go into receipt history. All open PRs return to Untangle and the beads reset.</p>
    <p>Queued actions, failed attempts, notes, strings and AI assessments stay unchanged. Nothing is posted to GitHub.</p>
    <button disabled={save.isPending} onClick={() => save.mutate()}>Tear and save</button>
    <button disabled={save.isPending} onClick={onClose}>Cancel</button>
    {save.error && <p role="alert">{save.error.message}</p>}
  </dialog>
}
export function Receipt() {
  const actions = useQuery({ queryKey: ['actions'], queryFn: () => api<Action[]>('/api/actions') })
  const policy = useQuery({ queryKey: ['apply-policy'], queryFn: () => api<{ enabled: boolean; sandboxRepo: string | null }>('/api/actions/policy') })
  const [confirm, setConfirm] = useState<Action | null>(null)
  const decisions = useQuery({ queryKey: ['decisions'], queryFn: () => api<Decision[]>('/api/decisions') })
  const history = useQuery({ queryKey: ['receipt-history'], queryFn: () => api<SavedRound[]>('/api/receipts/history') })
  const [tearConfirm, setTearConfirm] = useState(false)
  const [tearing, setTearing] = useState(false)
  const reducedMotion = useReducedMotion()
  const client = useQueryClient()
  const remove = useMutation({ mutationFn: (id: string) => api(`/api/actions/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['actions'] }) })
  const queued = actions.data?.filter(a => a.status === 'queued') ?? []
  const completed = actions.data?.filter(a => a.status === 'applied') ?? []
  const refreshRound = () => {
    void client.invalidateQueries({ queryKey: ['actions'] })
    void client.invalidateQueries({ queryKey: ['decisions'] })
    void client.invalidateQueries({ queryKey: ['receipt-history'] })
  }
  return <><aside className={`receipt ${tearing ? 'receipt-tearing' : ''}`} aria-label="Queued actions"
    onAnimationEnd={event => { if (event.target === event.currentTarget) { setTearing(false); refreshRound() } }}>
    <h2>TANGLE APPLY</h2><p>queued, nothing sent yet</p>
    {actions.error && <p role="alert">{actions.error.message}</p>}{policy.error && <p role="alert">{policy.error.message}</p>}
    {queued.length === 0 && <p>No queued actions.</p>}
    {queued.map(a => <div className="receipt-item" key={a.id}><b>{a.kind} · {a.prId}</b><span className="receipt-payload">{(() => {
      const p: { Text?: string; Reviewer?: string; HeadSha?: string } = JSON.parse(a.payload)
      return a.kind === 'merge' ? 'Merge captured PR version' : p.Text ?? p.Reviewer ?? 'Close without comment'
    })()}</span>
      <button onClick={() => remove.mutate(a.id)}>Remove</button>
      <button disabled={!policy.data?.enabled || a.prId.split('#')[0] !== policy.data.sandboxRepo} onClick={() => setConfirm(a)}>Apply</button>
    </div>)}<p className="receipt-total">TOTAL <b>{queued.length}</b></p>
    <p>{policy.data?.enabled ? `Apply restricted to ${policy.data.sandboxRepo ?? 'no sandbox'}.` : 'Apply disabled. Configure a sandbox first.'}</p>
    {actions.data?.filter(a => a.status !== 'queued' && a.status !== 'dismissed' && a.status !== 'applied')
      .map(a => <ActionResult action={a} key={a.id} />)}
    {completed.length > 0 && <details className="receipt-history"><summary>Completed actions ({completed.length})</summary>
      {completed.map(a => <ActionResult action={a} key={a.id} />)}</details>}
    <button className="tear-button" disabled={tearing || (!completed.length && !decisions.data?.length) || actions.data?.some(a => a.status === 'applying')}
      onClick={() => setTearConfirm(true)}>Tear and save</button>
    {decisions.error && <p role="alert">{decisions.error.message}</p>}
    {history.error && <p role="alert">{history.error.message}</p>}
    {(history.data?.length ?? 0) > 0 && <details className="receipt-history saved-receipts"><summary>Saved receipts ({history.data!.length})</summary>
      {history.data?.map(round => <details className="saved-round" key={round.id}>
        <summary>{new Date(round.createdAt).toLocaleString()} · {round.actions.length} applied, {round.decisions.length} decisions</summary>
        {round.actions.map(a => <ActionResult action={a} key={a.id} />)}
        {round.decisions.map(d => <p key={d.id}>{d.prId} · {d.choice === 'fix' && !d.actionId ? 'Still my turn' : d.choice}</p>)}
      </details>)}</details>}
    {remove.error && <p role="alert">{remove.error.message}</p>}
    {confirm && <Confirmation action={confirm} onClose={() => setConfirm(null)} />}</aside>
    {tearConfirm && <TearConfirmation onClose={() => setTearConfirm(false)}
      onSaved={() => { if (reducedMotion) refreshRound(); else setTearing(true) }} />}</>
}
