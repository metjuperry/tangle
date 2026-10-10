import { useQuery } from '@tanstack/react-query'
import { api, turnLabel, type Board, type Context } from '../api/client'
import { useBoardUi } from '../board/store'

export function SidePanel({ board }: { board: Board }) {
  const selected = useBoardUi(s => s.selection)
  const stringId = useBoardUi(s => s.stringId)
  const select = useBoardUi(s => s.select)
  const card = board.cards.find(p => p.id === selected)
  const context = useQuery({ queryKey: ['pr', selected], enabled: !!card,
    queryFn: () => api<Context>(`/api/prs/${card!.repoId}/${card!.number}`) })
  const yarn = board.strings.find(s => s.id === stringId)
  return <aside className="side-panel" aria-label="Card details">
    {yarn ? <><p className="eyebrow">Linked by a rule</p><h2>These belong together</h2><p>{yarn.reason}</p>
      {[yarn.aRef, yarn.bRef].map(ref => <button className="related-card" key={ref} onClick={() => select(ref.slice(3))}>{ref.slice(3)}</button>)}
      <p className="quiet">Read-only for now. Tying and cutting arrive in the next milestone.</p></> :
    card ? <><div className="panel-meta"><b>{card.id}</b><span>by {card.author ?? 'unknown'}</span><span>open {card.facts?.ageDays} days</span></div>
      <h2>{card.title}</h2><div className="chips"><span className={`turn-chip turn-${card.facts?.turn}`}><i />{turnLabel(card.facts?.turn)}</span>
        {card.facts?.origin === 'agent' && <span className="turn-chip">Agent-authored</span>}</div>
      <div className="fact-block"><p className="eyebrow">Whose turn</p><strong>{card.facts?.turnReason}</strong>
        <p>{card.facts?.size} · {card.facts?.idleDays} idle days · {card.facts?.conflicting ? 'conflicts' : 'no known conflicts'}</p>
        <p className="quiet">AI suggestions are off. Nothing will be posted to GitHub.</p></div>
      {context.isPending && <p role="status">Loading the card back…</p>}
      {context.error && <p role="alert">{context.error.message}</p>}
      {context.data && <>
        <h3>PR description</h3><p className="pr-text">{context.data.body || 'No description provided.'}</p>
        <h3>Open asks · {context.data.threads.length}</h3>{context.data.threads.length === 0 && <p className="quiet">No unresolved review threads.</p>}
        {context.data.threads.map(t => <div className="review-ask" key={t.id}><span className="ask-square" /><div><small>{t.lastActor ?? 'unknown'} · {t.lastAt ? new Date(t.lastAt).toLocaleDateString() : 'unknown date'}</small><p>{t.excerpt}</p></div></div>)}
        <h3>On the same string</h3>{context.data.strings.length === 0 && <p className="quiet">No strings yet.</p>}
        {context.data.strings.map(s => { const ref = s.aRef === `pr:${card.id}` ? s.bRef : s.aRef
          return <button className="related-card" key={s.id} onClick={() => select(ref.slice(3))}><b>{ref.slice(3)}</b><span>{s.reason}</span></button> })}
        {card.batchId && <><h3>In this batch</h3>{board.cards.filter(p => p.batchId === card.batchId).map(p => <button className="related-card" key={p.id} onClick={() => select(p.id)}>{p.id} · {p.title}</button>)}</>}
        <h3>Recent activity</h3>{context.data.events.slice(0, 8).map((e, i) => <p className="activity" key={i}>{e.actor ?? 'unknown'} · {e.kind} · {new Date(e.at).toLocaleDateString()}</p>)}
      </>}
    </> : <div className="panel-empty"><span className="empty-pin">●</span><h2>Follow a string.</h2><p>Pick a pinned card to read its story, open review asks and related pull requests.</p><p className="quiet">Your turn first. The dustiest next.</p></div>}
  </aside>
}
