import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'
import { sessionLabel, sessionMatchLabel, sessionUrl, type AgentSession } from './sessionDisplay'

export function Sessions({ prId }: { prId: string }) {
  const [id, setId] = useState('')
  const client = useQueryClient()
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: () => api<AgentSession[]>('/api/sessions') })
  const attach = useMutation({ mutationFn: () => write(`/api/sessions/${encodeURIComponent(id)}`, 'PATCH', { prId }),
    onSuccess: async () => { setId(''); await client.invalidateQueries({ queryKey: ['sessions'] }); await client.invalidateQueries({ queryKey: ['pr'] }) } })
  return <><h3>Agent sessions</h3>
    {sessions.data?.filter(session => session.prId === prId).map(session => <div className="related-card agent-session" key={session.id}>
      <b>{sessionLabel(session)}</b><span className="quiet">{sessionMatchLabel(session.matchedBy)}</span>
      {session.excerpt && <p className="session-excerpt">{session.excerpt}</p>}
      {sessionUrl(session.id) && <a href={sessionUrl(session.id)!} target="_blank" rel="noreferrer">Open session</a>}
      <details><summary>Session details</summary><code>{session.id}</code>
        {session.gitBranch && <p>Branch: {session.gitBranch}</p>}
        {!sessionUrl(session.id) && <p className="quiet">Local session; transcript stays on your machine.</p>}
      </details></div>)}
    <form onSubmit={event => { event.preventDefault(); attach.mutate() }}><label>Attach reported session<select value={id} onChange={event => setId(event.target.value)} required>
      <option value="">Choose a session</option>{sessions.data?.filter(session => session.prId !== prId).map(session => <option key={session.id} value={session.id}>
        {sessionLabel(session)} · {session.excerpt?.slice(0, 60) ?? (sessionUrl(session.id) ? new URL(session.id).pathname.split('/').filter(Boolean).at(-1)?.slice(0, 8) : session.id.slice(0, 8))}
      </option>)}</select></label>
      <button disabled={attach.isPending || !id}>Attach session</button></form>
    {(sessions.error || attach.error) && <p role="alert">{sessions.error?.message ?? attach.error?.message}</p>}</>
}
