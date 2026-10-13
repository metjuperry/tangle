import { useMutation, useQuery } from '@tanstack/react-query'
import { api, write } from '../api/client'
import { useState } from 'react'

export type AccountInfo = { mode: string; userId: string; isAdmin: boolean; login: string; name: string | null; tokenStatus: string; expiresAt: string | null; syncAvailable: boolean; installUrl: string }
type Installation = { account: string; url: string }
export function Account({ owners }: { owners: { id: string; watched: boolean }[] }) {
  const account = useQuery({ queryKey: ['account'], queryFn: () => api<AccountInfo>('/api/account') })
  const installations = useQuery({ queryKey: ['installations'], queryFn: () => api<Installation[]>('/api/account/installations'),
    enabled: account.data?.mode === 'GitHub' })
  const [confirmLogin, setConfirmLogin] = useState('')
  const remove = useMutation({ mutationFn: () => write('/api/account', 'DELETE', { login: confirmLogin }),
    onSuccess: () => window.location.assign('/') })
  const logout = useMutation({ mutationFn: async () => {
    const response = await fetch('/api/auth/logout', { method: 'POST' })
    if (!response.ok) throw new Error('Unable to sign out.')
  }, onSuccess: () => window.location.assign('/') })
  return <section className="settings-section" aria-labelledby="account-heading"><h2 id="account-heading">Account</h2>
    {account.isPending && <p role="status">Reading your account…</p>}
    {account.error && <p role="alert">{account.error.message}</p>}
    {account.data && <>
      <p><b>{account.data.name ?? account.data.login}</b> · {account.data.login}</p>
      {account.data.mode === 'GitHub' && <p className="quiet">GitHub user ID: {account.data.userId}</p>}
      <p>{account.data.mode === 'GitHub' ? `GitHub access: ${account.data.tokenStatus}.` : 'Local development-key mode.'}</p>
      {account.data.expiresAt && <p className="quiet">Token expires {new Date(account.data.expiresAt).toLocaleString()}.</p>}
      {!account.data.syncAvailable && <p className="provider-status">GitHub access expired, sign in again. Your cached board is retained.</p>}
      {account.data.mode === 'GitHub' && <>
        <a className="settings-evaluate-link" href="/auth/github/login">Sign in again</a>
        <h3>GitHub App installations</h3>
        {installations.isPending && <p role="status">Reading installations…</p>}
        {installations.error && <p role="alert">{installations.error.message}</p>}
        {installations.data?.map(installation => <p key={installation.account}>{installation.account}</p>)}
        {installations.data && owners.filter(owner => owner.watched && !installations.data.some(installation =>
          installation.account.toLowerCase() === owner.id.toLowerCase())).map(owner =>
          <p key={owner.id}>No GitHub App installation for watched owner <b>{owner.id}</b>. Ask its owner to install Tangle.</p>)}
        <a href={account.data.installUrl} target="_blank" rel="noopener noreferrer">Install Tangle on GitHub</a>
      </>}
      <p><button disabled={logout.isPending} onClick={() => logout.mutate()}>{logout.isPending ? 'Signing out…' : 'Sign out'}</button></p>
      {logout.error && <p role="alert">{logout.error.message}</p>}
      {account.data.mode === 'GitHub' && <div><h3>Delete my Tangle</h3>
        <p>This permanently removes your board, tokens, AI key and agent keys. It cannot be undone. Revoke Tangle's GitHub authorization afterwards.</p>
        <label>Type your login to delete<input value={confirmLogin} onChange={event => setConfirmLogin(event.target.value)} /></label>
        <button disabled={remove.isPending || confirmLogin !== account.data.login} onClick={() => remove.mutate()}>Delete my Tangle</button>
        {remove.error && <p role="alert">{remove.error.message}</p>}
        <a href="https://github.com/settings/apps/authorizations" target="_blank" rel="noopener noreferrer">Manage GitHub authorizations</a></div>}
    </>}
  </section>
}
