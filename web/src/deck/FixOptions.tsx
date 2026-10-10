import { useEffect, useRef } from 'react'

export function FixOptions({ reason, busy, error, onConfirm, onDraft, onClose }: {
  reason: string; busy: boolean; error?: string; onConfirm: () => void; onDraft: () => void; onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="fix-heading" onCancel={onClose}>
    <h2 id="fix-heading">Fix</h2><p>{reason}</p>
    <p>Acknowledge that this still needs your attention and move to the next card. The board keeps its computed turn unchanged.</p>
    <p className="quiet">No to-do list, queued action or GitHub post.</p>
    <div className="fix-options"><button autoFocus disabled={busy} onClick={onConfirm}>Still my turn</button>
      <button disabled={busy} onClick={onDraft}>Draft a comment instead</button>
      <button disabled={busy} onClick={onClose}>Cancel</button></div>
    {error && <p role="alert">{error}</p>}</dialog>
}
