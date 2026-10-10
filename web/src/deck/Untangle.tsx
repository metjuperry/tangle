import { useCallback, useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { api, write, type Board } from '../api/client'
import { ActionComposer, Receipt } from './Receipt'
import { actionLabel, friendlyAiText } from '../api/aiText'
import { activeDecisionIds } from './progress'

export type Decision = { id: string; prId: string; choice: string; actionId: string | null }
const choices = [['merge', 'Merge', 'M'], ['fix', 'Fix', 'F'], ['ping', 'Ping', 'P'], ['wait', 'Wait', 'W'], ['close', 'Close', 'X'], ['snooze', 'Snooze', 'S']]
export function Untangle({ board }: { board: Board }) {
  const decisions = useQuery({ queryKey: ['decisions'], queryFn: () => api<Decision[]>('/api/decisions') })
  const client = useQueryClient()
  const [composer, setComposer] = useState(false)
  const [composerKind, setComposerKind] = useState('comment')
  const [allowStillMyTurn, setAllowStillMyTurn] = useState(false)
  const [pendingChoice, setPendingChoice] = useState('fix')
  const [exitChoice, setExitChoice] = useState('')
  const reduced = useReducedMotion()
  const decided = activeDecisionIds(board.cards, decisions.data ?? [])
  const order = board.cards.filter(p => p.isMine).sort((a, b) => {
    const rank = (turn?: string) => turn === 'me' ? 0 : turn === 'nobody' ? 1 : 2
    return rank(a.facts?.turn) - rank(b.facts?.turn) ||
      (a.facts?.turn === 'nobody' ? (b.facts?.idleDays ?? 0) - (a.facts?.idleDays ?? 0) : (b.facts?.ageDays ?? 0) - (a.facts?.ageDays ?? 0)) || a.number - b.number
  })
  const card = order.find(p => !decided.has(p.id))
  const decide = useMutation({ mutationFn: ({ choice, actionId }: { choice: string; actionId?: string }) => {
    setExitChoice(choice)
    return write('/api/decisions', 'POST', { prId: card!.id, choice, actionId })
  }, onSuccess: async () => { await client.invalidateQueries({ queryKey: ['decisions'] }) } })
  const undo = useMutation({ mutationFn: () => write('/api/decisions/undo', 'POST', {}),
    onSuccess: async () => { await client.invalidateQueries({ queryKey: ['decisions'] }); await client.invalidateQueries({ queryKey: ['actions'] }) } })
  const choose = useCallback((choice: string) => {
    if (choice === 'wait' || choice === 'snooze') decide.mutate({ choice })
    else {
      decide.reset()
      setPendingChoice(choice)
      setAllowStillMyTurn(choice === 'fix')
      setComposerKind(choice === 'fix' ? 'still_my_turn' : choice === 'merge' ? 'merge' : choice === 'close' ? 'close' : 'request_review')
      setComposer(true)
    }
  }, [decide])
  useEffect(() => {
    const listener = (event: KeyboardEvent) => {
      if (event.target instanceof HTMLElement && (event.target.closest('input,textarea,select,button') || document.querySelector('dialog[open]'))) return
      if (event.key.toLowerCase() === 'u') { event.preventDefault(); if (!undo.isPending) undo.mutate(); return }
      const choice = choices.find(c => c[2].toLowerCase() === event.key.toLowerCase())
      if (choice && card && !decide.isPending) { event.preventDefault(); choose(choice[0]) }
    }
    window.addEventListener('keydown', listener); return () => window.removeEventListener('keydown', listener)
  }, [card, decide, undo, choose])
  const exit = reduced ? { opacity: 0 } : { opacity: 0, x: exitChoice === 'merge' ? 600 : exitChoice === 'close' ? -600 : 0,
    y: exitChoice === 'snooze' ? 500 : exitChoice === 'merge' || exitChoice === 'close' ? 0 : -500 }
  return <main className="untangle-workspace"><section className="deck" aria-label="Untangle deck">
    <p className="deck-progress">{decided.size} of {order.length} decided · your turn first, then the dustiest</p>
    {decisions.error && <p role="alert">{decisions.error.message}</p>}
    <AnimatePresence mode="wait"><motion.div className="deck-paper" key={card?.id ?? 'done'} initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={exit} transition={{ duration: reduced ? .1 : .3 }}>
      {card ? <><span className="deck-ref">{card.id}</span><h2>{card.title}</h2><p>{card.facts?.turnReason}</p>
        {card.verdict && <div className="fact-block"><p className="eyebrow">Suggested · {actionLabel(card.verdict.action)}</p><p>{friendlyAiText(card.verdict.reason)}</p></div>}
        <button onClick={() => { setPendingChoice('fix'); setAllowStillMyTurn(false); setComposerKind('comment'); setComposer(true) }}>Queue a GitHub action</button><p className="quiet">Fix can acknowledge “Still my turn” without posting. Merge, Ping and Close open an action editor. Nothing is sent yet.</p></> :
        <><h2>All {order.length} untangled</h2><p>Review your receipt before applying anything.</p><a href="/">Back to the board</a></>}
    </motion.div></AnimatePresence>
    <div className="verdict-bar">{choices.map(([choice, label, key]) => <button key={choice} disabled={!card || decide.isPending}
      className={card?.verdict?.action === choice ? 'suggested-choice' : ''} onClick={() => choose(choice)}>{label}<small>{key}</small></button>)}</div>
    <button disabled={!decisions.data?.length || undo.isPending} onClick={() => undo.mutate()}>Undo last decision · U</button>
    {((decide.error && !composer) || undo.error) && <p role="alert">{decide.error?.message ?? undo.error?.message}</p>}
    {composer && card && <ActionComposer prId={card.id} initialKind={composerKind} allowStillMyTurn={allowStillMyTurn}
      onClose={() => setComposer(false)} onQueued={actionId => decide.mutate({ choice: pendingChoice, actionId })}
      onLocalDecision={() => decide.mutateAsync({ choice: pendingChoice })} />}
  </section><Receipt /></main>
}
