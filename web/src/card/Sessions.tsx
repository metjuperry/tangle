import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'

export function Sessions({ prId }: { prId: string }) {
  const [id, setId] = useState('')
  const client = useQueryClient()
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: () => api<{ id: string; prId: string | null; matchedBy: string | null; excerpt: string | null }[]>('/api/sessions') })
  const attach = useMutation({ mutationFn: () => write(`/api/sessions/${encodeURIComponent(id)}`, 'PATCH', { prId }),
    onSuccess: async () => { setId(''); await client.invalidateQueries({ queryKey: ['sessions'] }); await client.invalidateQueries({ queryKey: ['pr'] }) } })
  return <><h3>Agent sessions</h3>
    {sessions.data?.filter(session => session.prId === prId).map(session => <div className="related-card" key={session.id}>
      <b>{session.id}</b><span>Matched by {session.matchedBy}</span>{session.excerpt && <p>{session.excerpt}</p>}</div>)}
    <form onSubmit={event => { event.preventDefault(); attach.mutate() }}><label>Attach reported session<select value={id} onChange={event => setId(event.target.value)} required>
      <option value="">Choose a session</option>{sessions.data?.filter(session => session.prId !== prId).map(session => <option key={session.id} value={session.id}>{session.id}</option>)}</select></label>
      <button disabled={attach.isPending || !id}>Attach session</button></form>
    {(sessions.error || attach.error) && <p role="alert">{sessions.error?.message ?? attach.error?.message}</p>}</>
}
