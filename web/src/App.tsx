import { useEffect, useState } from 'react'
import { QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import './App.css'

type Owner = { id: string; kind: 'org' | 'user'; watched: boolean }
type Repo = { id: string; watched: boolean; aiAllowed: boolean }
type Settings = { owners: Owner[]; repos: Repo[] }
type Card = { id: string; repoId: string; number: number; title: string; author: string | null; isMine: boolean; state: string; isDraft: boolean; updatedAt: string }
type Board = { cards: Card[] }
type SyncResult = { fetched: number; changed: number; calls: number; cost: number }
const queries = new QueryClient()
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (!response.ok) {
    const body: { error?: string } = await response.json()
    throw new Error(body.error ?? `Request failed (${response.status}).`)
  }
  return response.json() as Promise<T>
}
function SettingsPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api<Settings>('/api/settings') })
  const [draft, setDraft] = useState<Settings | null>(null)
  const [owner, setOwner] = useState('')
  const [kind, setKind] = useState<'org' | 'user'>('org')
  const [repo, setRepo] = useState('')
  const value = draft ?? settings.data
  const save = useMutation({
    mutationFn: (input: Settings) => api<Settings>('/api/settings', {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
    }),
    onSuccess: async () => { setDraft(null); await queryClient.invalidateQueries({ queryKey: ['settings'] }); await queryClient.invalidateQueries({ queryKey: ['board'] }) },
  })
  if (settings.error) return <p role="alert">{settings.error.message}</p>
  if (!value) return <p>Loading settings…</p>
  return <section><h1>Settings</h1><p>Tokens belong in user-secrets or your environment, never here. AI remains off until M5.</p>
    <h2>Watched owners</h2>
    {value.owners.map((o, i) => <label key={o.id}><span><input type="checkbox" checked={o.watched}
      onChange={e => setDraft({ ...value, owners: value.owners.map((item, j) => j === i ? { ...item, watched: e.target.checked } : item) })} /> {o.id} ({o.kind})</span></label>)}
    <form onSubmit={e => { e.preventDefault(); setDraft({ ...value, owners: [...value.owners, { id: owner.trim(), kind, watched: true }] }); setOwner('') }}>
      <label>Owner name<input value={owner} onChange={e => setOwner(e.target.value)} required /></label>
      <label>Owner type<select value={kind} onChange={e => setKind(e.target.value === 'user' ? 'user' : 'org')}><option value="org">Organization</option><option value="user">Personal account</option></select></label>
      <button>Add owner</button>
    </form>
    <h2>Repositories</h2><p>Watched owners discover their repos during sync. Uncheck a repo to hide it; add a repo to watch it without its entire owner.</p>
    {value.repos.map((r, i) => <label key={r.id}><span><input type="checkbox" checked={r.watched}
      onChange={e => setDraft({ ...value, repos: value.repos.map((item, j) => j === i ? { ...item, watched: e.target.checked } : item) })} /> {r.id} · AI off</span></label>)}
    <form onSubmit={e => { e.preventDefault(); setDraft({ ...value, repos: [...value.repos, { id: repo.trim(), watched: true, aiAllowed: false }] }); setRepo('') }}>
      <label>Repository (owner/repo)<input value={repo} onChange={e => setRepo(e.target.value)} required /></label><button>Add repository</button>
    </form>
    <button disabled={save.isPending} onClick={() => save.mutate(value)}>Save settings</button>
    {save.error && <p role="alert">{save.error.message}</p>}{save.isSuccess && <p role="status">Settings saved.</p>}
  </section>
}
function PrTable({ cards }: { cards: Card[] }) {
  return <table><thead><tr><th>PR</th><th>Title</th><th>Author</th><th>State</th><th>Updated</th></tr></thead>
    <tbody>{cards.map(c => <tr key={c.id}><td><a href={`https://github.com/${c.repoId}/pull/${c.number}`} rel="noreferrer" target="_blank">{c.id}</a></td>
      <td>{c.title}{c.isDraft && ' (draft)'}</td><td>{c.author}{c.isMine && ' (you)'}</td><td>{c.state}</td><td>{c.updatedAt}</td></tr>)}</tbody></table>
}
function Workspace() {
  const queryClient = useQueryClient()
  const path = window.location.pathname
  const board = useQuery({ queryKey: ['board'], queryFn: () => api<Board>('/api/board') })
  const debug = useQuery({ queryKey: ['debug'], queryFn: () => api<Card[]>('/api/debug/prs'), enabled: path === '/debug/prs' })
  const sync = useMutation({
    mutationFn: () => api<SyncResult>('/api/sync', { method: 'POST' }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['board'] }); await queryClient.invalidateQueries({ queryKey: ['debug'] }); await queryClient.invalidateQueries({ queryKey: ['settings'] }) },
  })
  return <><nav><a href="/">Board</a> · <a href="/settings">Settings</a> · <a href="/debug/prs">Debug PRs</a> <button disabled={sync.isPending} onClick={() => sync.mutate()}>{sync.isPending ? 'Syncing…' : 'Sync'}</button></nav>
    {sync.error && <p role="alert">{sync.error.message}</p>}{sync.data && <p role="status">Synced {sync.data.fetched} PRs; {sync.data.changed} changed; {sync.data.cost} search points.</p>}
    {path === '/settings' ? <SettingsPage /> : <section className="backlog"><h1>{path === '/debug/prs' ? 'Stored pull requests' : 'Your yarn board'}</h1>
      <p>The React Flow board arrives in M3. This milestone shows synced data as a plain table.</p>
      {(board.error ?? debug.error) && <p role="alert">{(board.error ?? debug.error)?.message}</p>}
      {board.isPending ? <p>Loading pull requests…</p> : board.data?.cards.length === 0 ? <p>No pull requests synced yet.</p> : <PrTable cards={path === '/debug/prs' ? debug.data ?? [] : board.data?.cards ?? []} />}
    </section>}</>
}
function Shell() {
  const [authenticated, setAuthenticated] = useState(false)
  const [key, setKey] = useState('')
  const [error, setError] = useState('')
  useEffect(() => {
    void fetch('/api/auth').then(r => setAuthenticated(r.ok)).catch(() => setError('Unable to reach Tangle.'))
  }, [])
  async function login(event: React.SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ key }),
      })
      if (!response.ok) { setError('Invalid development key.'); return }
      setKey(''); setError(''); setAuthenticated(true)
    } catch { setError('Unable to reach Tangle.') }
  }
  return <><header><strong>tangle</strong><span>Pull requests, untangled.</span></header>
    <main>{authenticated ? <Workspace /> : <section><h1>Open your board</h1><form onSubmit={login}>
      <label>Development key<input type="password" value={key} onChange={e => setKey(e.target.value)} required autoComplete="current-password" /></label>
      <button>Unlock Tangle</button>
    </form></section>}{error && <p role="alert">{error}</p>}</main></>
}
export default function App() {
  return <QueryClientProvider client={queries}><Shell /></QueryClientProvider>
}
