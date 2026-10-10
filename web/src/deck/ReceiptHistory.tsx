import { useEffect, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { actionLabel } from '../api/aiText'
import { ActionResult, type Action } from './Receipt'
import type { Decision } from './Untangle'

type SavedRound = { id: string; createdAt: string; actions: Action[]; decisions: Decision[] }

export function ReceiptHistory({ onClose }: { onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const history = useQuery({ queryKey: ['receipt-history'], queryFn: () => api<SavedRound[]>('/api/receipts/history') })
  useEffect(() => { dialog.current?.showModal() }, [])
  return <dialog ref={dialog} className="new-sticky-dialog receipt-history-dialog" aria-labelledby="receipt-history-heading" onCancel={onClose}>
    <div className="history-heading"><h2 id="receipt-history-heading">Receipt history</h2><button onClick={onClose}>Close</button></div>
    <p>Saved triage rounds. These actions have already been applied and cannot be applied again.</p>
    {history.isPending && <p role="status">Loading saved receipts…</p>}
    {history.error && <p role="alert">{history.error.message}</p>}
    {history.data?.length === 0 && <p>No saved receipts yet. Tear &amp; save an applied receipt to keep it here.</p>}
    {history.data?.map(round => <details className="saved-round" key={round.id}>
      <summary>{new Date(round.createdAt).toLocaleString()} · {round.actions.length} applied, {round.decisions.length} decisions</summary>
      {round.actions.map(action => <ActionResult action={action} key={action.id} />)}
      {round.decisions.length > 0 && <><h3>Decisions</h3>{round.decisions.map(decision => <p key={decision.id}>
        {decision.prId} · {decision.choice === 'fix' && !decision.actionId ? 'Still my turn' : actionLabel(decision.choice)}
      </p>)}</>}
    </details>)}
  </dialog>
}
