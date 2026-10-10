import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'

export type Action = { id: string; prId: string; kind: string; payload: string; status: string; result: string | null }
export function ActionComposer({ prId, onClose, onQueued, initialKind = 'comment', onLocalDecision }: { prId: string; onClose: () => void; onQueued?: (id: string) => void; initialKind?: string; onLocalDecision?: () => void }) {
  const [kind, setKind] = useState(initialKind)
  const [text, setText] = useState('')
  const [reviewer, setReviewer] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  const client = useQueryClient()
  useEffect(() => { dialog.current?.showModal() }, [])
  const save = useMutation({ mutationFn: () => write<Action>('/api/actions', 'POST', { prId, kind,
    payload: kind === 'comment' ? { text } : kind === 'request_review' ? { reviewer } : {} }),
    onSuccess: async action => { await client.invalidateQueries({ queryKey: ['actions'] }); onQueued?.(action.id); onClose() } })
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="queue-heading" onCancel={onClose}>
    <h2 id="queue-heading">Queue action · {prId}</h2><p>Nothing is posted until you confirm Apply on the receipt.</p>
    <form onSubmit={e => { e.preventDefault(); save.mutate() }}>
      <label>Action kind<select value={kind} onChange={e => setKind(e.target.value)}><option value="comment">Post comment</option><option value="request_review">Request review</option><option value="close">Close PR</option><option value="merge">Merge PR</option></select></label>
      {kind === 'comment' && <label>Comment text<textarea value={text} onChange={e => setText(e.target.value)} required /></label>}
      {kind === 'request_review' && <label>Reviewer login<input value={reviewer} onChange={e => setReviewer(e.target.value)} required /></label>}
      {kind === 'merge' && <p>Tangle captures the current PR version from GitHub when you queue this merge. If new commits arrive before Apply, you’ll need to queue it again.</p>}
      {kind === 'close' && <p>Closes this PR without posting a comment. Queue a separate comment if needed.</p>}
      <button disabled={save.isPending}>{save.isPending && kind === 'merge' ? 'Checking PR version…' : 'Queue action'}</button><button type="button" disabled={save.isPending} onClick={onClose}>Cancel</button>
      {onLocalDecision && <button type="button" onClick={onLocalDecision}>Record decision without GitHub action</button>}
      {save.error && <p role="alert">{save.error.message}</p>}
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
export function Receipt() {
  const actions = useQuery({ queryKey: ['actions'], queryFn: () => api<Action[]>('/api/actions') })
  const policy = useQuery({ queryKey: ['apply-policy'], queryFn: () => api<{ enabled: boolean; sandboxRepo: string | null }>('/api/actions/policy') })
  const [confirm, setConfirm] = useState<Action | null>(null)
  const client = useQueryClient()
  const remove = useMutation({ mutationFn: (id: string) => api(`/api/actions/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['actions'] }) })
  const queued = actions.data?.filter(a => a.status === 'queued') ?? []
  return <aside className="receipt" aria-label="Queued actions"><h2>TANGLE APPLY</h2><p>queued, nothing sent yet</p>
    {actions.error && <p role="alert">{actions.error.message}</p>}{policy.error && <p role="alert">{policy.error.message}</p>}
    {queued.length === 0 && <p>Decisions land here.</p>}
    {queued.map(a => <div className="receipt-item" key={a.id}><b>{a.kind} · {a.prId}</b><span className="receipt-payload">{(() => {
      const p: { Text?: string; Reviewer?: string; HeadSha?: string } = JSON.parse(a.payload)
      return a.kind === 'merge' ? 'Merge captured PR version' : p.Text ?? p.Reviewer ?? 'Close without comment'
    })()}</span>
      <button onClick={() => remove.mutate(a.id)}>Remove</button>
      <button disabled={!policy.data?.enabled || a.prId.split('#')[0] !== policy.data.sandboxRepo} onClick={() => setConfirm(a)}>Apply</button>
    </div>)}<p className="receipt-total">TOTAL <b>{queued.length}</b></p>
    <p>{policy.data?.enabled ? `Apply restricted to ${policy.data.sandboxRepo ?? 'no sandbox'}.` : 'Apply disabled. Configure a sandbox first.'}</p>
    {actions.data?.filter(a => a.status !== 'queued' && a.status !== 'dismissed').map(a => <p key={a.id}>{a.prId}: {a.status} · {a.result}</p>)}
    {remove.error && <p role="alert">{remove.error.message}</p>}
    {confirm && <Confirmation action={confirm} onClose={() => setConfirm(null)} />}</aside>
}
