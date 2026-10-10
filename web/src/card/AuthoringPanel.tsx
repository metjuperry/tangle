import { useEffect, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { write, type Board, type Sticky, type Yarn } from '../api/client'
import { useBoardUi } from '../board/store'

export function Notes({ board, targetRef }: { board: Board; targetRef: string }) {
  const [text, setText] = useState('')
  const client = useQueryClient()
  const save = useMutation({ mutationFn: () => write('/api/notes', 'POST', { targetRef, text }),
    onSuccess: async () => { setText(''); await client.invalidateQueries({ queryKey: ['board'] }) } })
  return <><h3>Your notes</h3>{(board.notes ?? []).filter(n => n.targetRef === targetRef).map(n => <p className="handwriting" key={n.id}>{n.text}</p>)}
    <form onSubmit={e => { e.preventDefault(); save.mutate() }}><label>Add a note<textarea value={text} onChange={e => setText(e.target.value)} required /></label>
      <button disabled={save.isPending}>Pin it</button></form>{save.error && <p role="alert">{save.error.message}</p>}</>
}
export function StringEditor({ yarn, board }: { yarn: Yarn; board: Board }) {
  const [label, setLabel] = useState(yarn.label ?? '')
  const client = useQueryClient()
  const select = useBoardUi(s => s.select)
  const mutation = useMutation({ mutationFn: (body: object) => write('/api/links/' + yarn.id, 'PATCH', body),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['board'] }); await client.invalidateQueries({ queryKey: ['pr'] }) } })
  return <><p className="eyebrow">{yarn.source === 'me' ? 'Tied by you' : 'Linked by a rule'}</p><h2>These belong together</h2>
    <p>{yarn.reason}</p><form onSubmit={e => { e.preventDefault(); mutation.mutate({ label }) }}>
      <label>Label the string<input value={label} onChange={e => setLabel(e.target.value)} placeholder="same customer release" /></label><button>Save label</button></form>
    {yarn.label && <p className="handwriting">{yarn.label}</p>}
    {[yarn.aRef, yarn.bRef].map(ref => <button className="related-card" key={ref} onClick={() => select(ref.startsWith('sticky:') ? ref : ref.slice(3))}>{ref.replace(/^pr:/, '')}</button>)}
    <button onClick={() => mutation.mutate({ status: yarn.status === 'cut' ? 'tied' : 'cut' })}>{yarn.status === 'cut' ? 'Re-tie string' : 'Cut the string'}</button>
    {mutation.error && <p role="alert">{mutation.error.message}</p>}
    <Notes board={board} targetRef={`link:${yarn.id}`} /></>
}
export function StickyEditor({ sticky, board }: { sticky: Sticky; board: Board }) {
  const [title, setTitle] = useState(sticky.title)
  const [color, setColor] = useState(sticky.color)
  const client = useQueryClient()
  const startTie = useBoardUi(s => s.startTie)
  const save = useMutation({ mutationFn: (archived: boolean) => write('/api/stickies/' + sticky.id, 'PATCH', { title, color, archived }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['board'] }) })
  return <><h2>Sticky note</h2><form onSubmit={e => { e.preventDefault(); save.mutate(false) }}>
    <label>Sticky title<input value={title} onChange={e => setTitle(e.target.value)} required /></label>
    <label>Sticky color<select value={color} onChange={e => setColor(e.target.value)}>{['yellow', 'blue', 'pink', 'green'].map(c => <option key={c}>{c}</option>)}</select></label><button>Save sticky</button></form>
    <p>{sticky.merged} of {sticky.total} tied PRs merged</p>
    {sticky.total > 0 && sticky.merged === sticky.total && <p>All tied PRs are merged. Ready to archive?</p>}
    <button onClick={() => startTie(`sticky:${sticky.id}`)}>Tie a string</button><button onClick={() => save.mutate(true)}>Archive sticky</button>
    {save.error && <p role="alert">{save.error.message}</p>}<Notes board={board} targetRef={`sticky:${sticky.id}`} /></>
}
export function NewSticky({ onClose }: { onClose: () => void }) {
  const [title, setTitle] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])
  const client = useQueryClient()
  const select = useBoardUi(s => s.select)
  const save = useMutation({ mutationFn: () => write<Sticky>('/api/stickies', 'POST', { title, color: 'yellow' }),
    onSuccess: async sticky => { setTitle(''); await client.invalidateQueries({ queryKey: ['board'] }); select(`sticky:${sticky.id}`); onClose() } })
  return <dialog ref={dialog} className="new-sticky-dialog" aria-labelledby="new-sticky-heading" onCancel={onClose}>
    <h2 id="new-sticky-heading">New sticky</h2>
    <form onSubmit={e => { e.preventDefault(); save.mutate() }}><label>New sticky title<input autoFocus value={title} onChange={e => setTitle(e.target.value)} required maxLength={300} /></label>
      <div><button disabled={save.isPending}>Create sticky</button><button type="button" onClick={onClose}>Cancel</button></div>
      {save.error && <p role="alert">{save.error.message}</p>}</form></dialog>
}
