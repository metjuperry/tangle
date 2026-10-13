import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'
import type { AccountInfo } from './Account'

type User = { id: string; login: string; lastSeenAt: string; disabled: boolean; companyGranted: boolean }
type AdminData = { users: User[]; logins: string[]; organizations: string[]; companyEnabled: boolean }
export function AdminSettings() {
  const client = useQueryClient()
  const account = useQuery({ queryKey: ['account'], queryFn: () => api<AccountInfo>('/api/account') })
  const data = useQuery({ queryKey: ['admin'], queryFn: () => api<AdminData>('/api/account/admin'), retry: false, enabled: account.data?.isAdmin === true })
  const [logins, setLogins] = useState<string | null>(null)
  const [orgs, setOrgs] = useState<string | null>(null)
  const save = useMutation({ mutationFn: (input: object) => write('/api/account/admin/user', 'PUT', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['admin'] }) })
  const allowlist = useMutation({ mutationFn: () => write('/api/account/admin/allowlist', 'PUT', {
    logins: (logins ?? data.data!.logins.join(',')).split(',').map(value => value.trim()).filter(Boolean),
    organizations: (orgs ?? data.data!.organizations.join(',')).split(',').map(value => value.trim()).filter(Boolean),
  }), onSuccess: () => client.invalidateQueries({ queryKey: ['admin'] }) })
  if (data.error) return <p role="alert">{data.error.message}</p>
  if (!data.data) return null
  return <section className="settings-section" aria-labelledby="admin-heading"><h2 id="admin-heading">Administration</h2>
    <p>Manage admission and funding only. Other users' boards are never available here.</p>
    <label>Allowed GitHub logins<input value={logins ?? data.data.logins.join(', ')} onChange={event => setLogins(event.target.value)} /></label>
    <label>Allowed organizations<input value={orgs ?? data.data.organizations.join(', ')} onChange={event => setOrgs(event.target.value)} /></label>
    <button disabled={allowlist.isPending} onClick={() => allowlist.mutate()}>Save allowlist</button>
    {data.data.users.map(user => <AdminUser key={user.id} user={user} companyEnabled={data.data.companyEnabled} pending={save.isPending} save={input => save.mutate(input)} />)}
    {(save.error || allowlist.error) && <p role="alert">{save.error?.message ?? allowlist.error?.message}</p>}
  </section>
}
function AdminUser({ user, pending, companyEnabled, save }: { user: User; pending: boolean; companyEnabled: boolean; save: (input: object) => void }) {
  const [budget, setBudget] = useState(1)
  return <div className="settings-row"><div><b>{user.login}</b><p>Last seen {user.lastSeenAt}</p>
    <label>Company budget for {user.login}<input type="number" min="0" max="100" value={budget} onChange={event => setBudget(Number(event.target.value))} /></label></div>
    <button disabled={pending} onClick={() => save({ ...user, disabled: !user.disabled, budget })}>{user.disabled ? 'Enable' : 'Disable'} {user.login}</button>
    {companyEnabled && <button disabled={pending} onClick={() => save({ ...user, companyGranted: !user.companyGranted, budget })}>{user.companyGranted ? 'Revoke company AI' : 'Grant company AI'} for {user.login}</button>}
  </div>
}
