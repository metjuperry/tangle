import { useQuery } from '@tanstack/react-query'
import { api, turnLabel, type Board, type Context } from '../api/client'
import { useBoardUi } from '../board/store'
import { Notes, StickyEditor, StringEditor } from './AuthoringPanel'
import { findStacks } from '../board/stacks'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { write } from '../api/client'

export function SidePanel({ board }: { board: Board }) {
  const selected = useBoardUi(s => s.selection)
  const stringId = useBoardUi(s => s.stringId)
  const select = useBoardUi(s => s.select)
  const startTie = useBoardUi(s => s.startTie)
  const selectString = useBoardUi(s => s.selectString)
  const card = board.cards.find(p => p.id === selected)
  const client = useQueryClient()
  const stack = findStacks(board.cards).find(group => group.some(p => p.id === selected))
  const separated = stack?.some(p => board.positions?.some(position => position.nodeRef === `pr:${p.id}`))
  const regroup = useMutation({
    mutationFn: () => write('/api/positions/reset', 'POST', { nodeRefs: stack!.map(p => `pr:${p.id}`) }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['board'] }),
  })
  const context = useQuery({ queryKey: ['pr', selected], enabled: !!card,
    queryFn: () => api<Context>(`/api/prs/${card!.repoId}/${card!.number}`) })
  const yarn = [...board.strings, ...(board.cutStrings ?? [])].find(s => s.id === stringId)
  const sticky = board.stickies?.find(s => `sticky:${s.id}` === selected)
  return <aside className="side-panel" aria-label="Card details">
    {yarn ? <StringEditor key={yarn.id} yarn={yarn} board={board} /> : sticky ? <StickyEditor key={sticky.id} sticky={sticky} board={board} /> :
    card ? <><div className="panel-meta"><b>{card.id}</b><span>by {card.author ?? 'unknown'}</span><span>open {card.facts?.ageDays} days</span></div>
      <h2>{card.title}</h2><div className="chips"><span className={`turn-chip turn-${card.facts?.turn}`}><i />{turnLabel(card.facts?.turn)}</span>
        {card.facts?.origin === 'agent' && <span className="turn-chip">Agent-authored</span>}</div>
      <button onClick={() => startTie(`pr:${card.id}`)}>Tie a string</button>
      {separated && <div><button disabled={regroup.isPending} onClick={() => regroup.mutate()}>Group stack again</button>
        <p className="quiet">Restores this stack’s automatic layout; notes and strings stay intact.</p>
        {regroup.error && <p role="alert">{regroup.error.message}</p>}</div>}
      <Notes key={card.id} board={board} targetRef={`pr:${card.id}`} />
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
          return <button className="related-card" key={s.id} onClick={() => select(ref.startsWith('sticky:') ? ref : ref.slice(3))}><b>{ref.replace(/^pr:/, '')}</b><span>{s.label ?? s.reason}</span></button> })}
        {card.batchId && <><h3>In this batch</h3>{board.cards.filter(p => p.batchId === card.batchId).map(p => <button className="related-card" key={p.id} onClick={() => select(p.id)}>{p.id} · {p.title}</button>)}</>}
        <h3>Recent activity</h3>{context.data.events.slice(0, 8).map((e, i) => <p className="activity" key={i}>{e.actor ?? 'unknown'} · {e.kind} · {new Date(e.at).toLocaleDateString()}</p>)}
      </>}
    </> : <div className="panel-empty"><span className="empty-pin">●</span><h2>Follow a string.</h2><p>Pick a pinned card to read its story, open review asks and related pull requests.</p><p className="quiet">Your turn first. The dustiest next.</p></div>}
    {(board.cutStrings?.length ?? 0) > 0 && <><h3>Cut strings</h3>{board.cutStrings?.map(s =>
      <button className="related-card" key={s.id} onClick={() => selectString(s.id)}>{s.aRef} — {s.bRef}</button>)}</>}
  </aside>
}
