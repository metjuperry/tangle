import { useEffect, useRef, useState } from 'react'
import { QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import './App.css'
import { api, type Board } from './api/client'
import { BoardView } from './board/BoardView'
import { SidePanel } from './card/SidePanel'
import { useBoardUi } from './board/store'
import { useLiveEvents } from './api/events'
import { NewSticky } from './card/AuthoringPanel'

type Owner = { id: string; kind: 'org' | 'user'; watched: boolean }
type Repo = { id: string; watched: boolean; aiAllowed: boolean }
type Settings = { owners: Owner[]; repos: Repo[]; botLogins: string[]; aiBudget: number }
type SyncResult = { fetched: number; changed: number; calls: number; cost: number }
const queries = new QueryClient()
function SettingsPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api<Settings>('/api/settings') })
  const aiStatus = useQuery({ queryKey: ['ai'], queryFn: () => api<{ enabled: boolean; calls: { cost: number; status: string; error: string | null; created_at: string }[] }>('/api/ai/status') })
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
  return <section className="settings-page"><h1>Settings</h1><p>Tokens belong in user-secrets or your environment, never here.</p>
    <h2>AI privacy and budget</h2><p>AI is opt-in per repository. Enabling a switch permits sending its titles, descriptions, changed paths, review excerpts and linked labels to Anthropic. Never diffs or file contents. Check client policy first.</p>
    <p>{aiStatus.data?.enabled ? 'Provider calls enabled on the server.' : 'Provider calls disabled on the server. Set Ai:Enabled only when ready to authorize paid API usage.'}</p>
    <label>Monthly AI budget (USD)<input type="number" min="0" max="100" step="0.01" value={value.aiBudget ?? 1}
      onChange={e => setDraft({ ...value, aiBudget: Number(e.target.value) })} /></label>
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
      onChange={e => setDraft({ ...value, repos: value.repos.map((item, j) => j === i ? { ...item, watched: e.target.checked } : item) })} /> {r.id}</span>
      <span><input aria-label={`Allow AI for ${r.id}`} type="checkbox" checked={r.aiAllowed}
        onChange={e => setDraft({ ...value, repos: value.repos.map((item, j) => j === i ? { ...item, aiAllowed: e.target.checked } : item) })} /> Allow AI for this repo</span></label>)}
    <form onSubmit={e => { e.preventDefault(); setDraft({ ...value, repos: [...value.repos, { id: repo.trim(), watched: true, aiAllowed: false }] }); setRepo('') }}>
      <label>Repository (owner/repo)<input value={repo} onChange={e => setRepo(e.target.value)} required /></label><button>Add repository</button>
    </form>
    <button disabled={save.isPending} onClick={() => save.mutate(value)}>Save settings</button>
    <label>Bot logins (comma-separated)<input value={value.botLogins.join(', ')}
      onChange={e => setDraft({ ...value, botLogins: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })} /></label>
    {save.error && <p role="alert">{save.error.message}</p>}{save.isSuccess && <p role="status">Settings saved.</p>}
    <h3>Recent AI calls</h3>{aiStatus.error && <p role="alert">{aiStatus.error.message}</p>}
    {aiStatus.data?.calls.map((call, i) => <p key={i}>{call.status} · ${call.cost.toFixed(6)} · {call.error ?? call.created_at}</p>)}
  </section>
}
function Workspace() {
  const [creatingSticky, setCreatingSticky] = useState(false)
  const queryClient = useQueryClient()
  const path = window.location.pathname
  const board = useQuery({ queryKey: ['board'], queryFn: () => api<Board>('/api/board') })
  const status = useLiveEvents()
  const dimOthers = useBoardUi(s => s.dimOthers)
  const toggleOthers = useBoardUi(s => s.toggleOthers)
  const attemptedAutoSync = useRef(false)
  const sync = useMutation({
    mutationFn: () => api<SyncResult>('/api/sync', { method: 'POST' }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['board'] }); await queryClient.invalidateQueries({ queryKey: ['pr'] }); await queryClient.invalidateQueries({ queryKey: ['settings'] }) },
  })
  const mutate = sync.mutate
  useEffect(() => {
    if (path !== '/' || !board.data || attemptedAutoSync.current) return
    attemptedAutoSync.current = true
    if (!board.data.lastSyncAt || Date.now() - Date.parse(board.data.lastSyncAt) > 10 * 60 * 1000) mutate()
  }, [board.data, path, mutate])
  const mine = board.data?.cards.filter(p => p.isMine).length ?? 0
  const others = (board.data?.cards.length ?? 0) - mine
  return <><header className="top-bar"><a className="brand" href="/" aria-label="Tangle board"><svg width="30" height="30" viewBox="0 0 30 30" fill="none" aria-hidden="true"><path d="M4 22C10 4 22 4 18 14S6 16 15 11 28 14 26 24" /></svg>tangle</a>
    <nav className="view-switch"><a href="/" aria-current={path === '/' ? 'page' : undefined}>Board</a><span title="Arrives in M6">Untangle</span></nav>
    <div className="header-counts"><span><b>{mine}</b> yours</span><span><b>{others}</b> around you</span><span><b>{board.data?.strings.length ?? 0}</b> strings</span></div>
    <div className="header-spacer" /><span className="knot-meter">Untied 0 of {mine}<span aria-hidden="true" className="knots">{Array.from({ length: Math.min(mine, 20) }, (_, i) => <i key={i} />)}</span></span>
    <button className="header-button" disabled={others === 0} aria-pressed={others > 0 && dimOthers} onClick={toggleOthers}
      title={others === 0 ? 'Every PR on this board is authored by you.' : 'Fade cards authored by other people, not cards waiting on a reviewer.'}>
      {others === 0 ? 'No others’ PRs to dim' : dimOthers ? 'Show others’ PRs' : 'Dim others’ PRs'}</button>
    {path === '/' && <button className="header-button" onClick={() => setCreatingSticky(true)}>New sticky</button>}
    <button className="header-button" disabled={sync.isPending} onClick={() => sync.mutate()}>{sync.isPending ? 'Syncing…' : 'Sync'}</button><a className="header-button" href="/settings">Settings</a></header>
    {creatingSticky && <NewSticky onClose={() => setCreatingSticky(false)} />}
    {(status || sync.error) && <div className={`notice ${sync.error ? 'notice-error' : ''}`} role={sync.error ? 'alert' : 'status'}>{sync.error?.message ?? status}</div>}
    {path === '/settings' ? <main className="settings-wrap"><SettingsPage /></main> :
      path !== '/' ? <main className="settings-wrap"><h1>Page not found</h1><a href="/">Back to the board</a></main> :
      board.error ? <main className="settings-wrap"><p role="alert">{board.error.message}</p></main> :
      board.data ? <main className="board-workspace"><BoardView board={board.data} /><SidePanel board={board.data} />
        {board.data.cards.length === 0 && <div className="board-empty"><h1>Your yarn board</h1><p>No pull requests synced yet.</p><a href="/settings">Choose watched repositories</a></div>}</main> :
      <main className="settings-wrap"><p role="status">Loading your board…</p></main>}</>
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
  if (authenticated) return <Workspace />
  return <><header className="top-bar"><strong className="brand">tangle</strong><span>Pull requests, untangled.</span></header>
    <main className="login-wrap"><section className="login-paper"><h1>Open your board</h1><form onSubmit={login}>
      <label>Development key<input type="password" value={key} onChange={e => setKey(e.target.value)} required autoComplete="current-password" /></label>
      <button>Unlock Tangle</button>
    </form></section>{error && <p role="alert">{error}</p>}</main></>
}
export default function App() {
  return <QueryClientProvider client={queries}><Shell /></QueryClientProvider>
}
