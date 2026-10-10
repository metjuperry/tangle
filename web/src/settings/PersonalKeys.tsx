import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'

type KeyInfo = { id: string; name: string; createdAt: string; revokedAt: string | null }
export function PersonalKeys() {
  const [name, setName] = useState('')
  const [secret, setSecret] = useState<string | null>(null)
  const client = useQueryClient()
  const keys = useQuery({ queryKey: ['keys'], queryFn: () => api<KeyInfo[]>('/api/keys') })
  const create = useMutation({ mutationFn: () => write<{ key: string }>('/api/keys', 'POST', { name }),
    onSuccess: async result => { setSecret(result.key); setName(''); create.reset(); await client.invalidateQueries({ queryKey: ['keys'] }) } })
  const revoke = useMutation({ mutationFn: (id: string) => api(`/api/keys/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['keys'] }) })
  return <section className="settings-section" aria-labelledby="personal-keys-heading"><h2 id="personal-keys-heading">Agent personal keys</h2>
    <p>Separate keys for MCP and session reporting. They cannot apply actions or access Settings. Keep them outside your repo.</p>
    <form className="settings-add-form" onSubmit={event => { event.preventDefault(); create.mutate() }}>
      <label>Personal key name<input value={name} onChange={event => setName(event.target.value)} required maxLength={100} /></label>
      <button disabled={create.isPending}>Create personal key</button></form>
    {secret && <div><label>New personal key — shown once<input readOnly value={secret} autoComplete="off" /></label>
      <button onClick={() => setSecret(null)}>Hide key</button><p className="quiet">Set this as TANGLE_PERSONAL_KEY for the hook and Bearer authorization for MCP.</p></div>}
    {keys.data?.map(key => <div className="settings-row" key={key.id}><span>{key.name} · {key.revokedAt ? 'Revoked' : 'Active'}</span>
      {!key.revokedAt && <button onClick={() => revoke.mutate(key.id)}>Revoke {key.name}</button>}</div>)}
    {(keys.error || create.error || revoke.error) && <p role="alert">{keys.error?.message ?? create.error?.message ?? revoke.error?.message}</p>}
  </section>
}
