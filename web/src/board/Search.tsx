import { useEffect, useRef, useState } from 'react'
import type { Board } from '../api/client'
import { useBoardUi } from './store'

export function Search({ board }: { board: Board }) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const dialog = useRef<HTMLDialogElement>(null)
  const focus = useBoardUi(s => s.focus)
  useEffect(() => {
    const listener = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault(); setOpen(true)
      }
    }
    window.addEventListener('keydown', listener); return () => window.removeEventListener('keydown', listener)
  }, [])
  useEffect(() => { if (open) dialog.current?.showModal() }, [open])
  const matches = board.cards.filter(card => `${card.id} ${card.title}`.toLowerCase().includes(query.toLowerCase())).slice(0, 20)
  return <><button className="board-search-button" onClick={() => setOpen(true)}>Find a PR <kbd>Ctrl K</kbd></button>
    {open && <dialog ref={dialog} className="new-sticky-dialog search-dialog" aria-labelledby="search-heading" onCancel={() => setOpen(false)}>
      <h2 id="search-heading">Find a pull request</h2><label>Search titles or PR numbers<input autoFocus value={query} onChange={event => setQuery(event.target.value)} /></label>
      <div className="search-results">{matches.map(card => <button key={card.id} onClick={() => { focus(card.id); setOpen(false); setQuery('') }}>
        <b>{card.id}</b><span>{card.title}</span></button>)}</div>
      {matches.length === 0 && <p>No matching PRs.</p>}<button onClick={() => setOpen(false)}>Close search</button>
    </dialog>}</>
}
