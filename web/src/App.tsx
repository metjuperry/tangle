import { lazy, Suspense, useEffect, useRef, useState, useTransition } from 'react'
import { QueryClient, QueryClientProvider, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import './App.css'
import { api, type Board } from './api/client'
import { BoardView } from './board/BoardView'
import { SidePanel } from './card/SidePanel'
import { useBoardUi } from './board/store'
import { useLiveEvents } from './api/events'
import { NewSticky } from './card/AuthoringPanel'
import type { Decision } from './deck/Untangle'
import type { Action } from './deck/Receipt'
import { ViewLoading } from './theme/ViewLoading'
import { activeDecisionIds } from './deck/progress'
import { ReceiptHistory } from './deck/ReceiptHistory'
import { PersonalKeys } from './settings/PersonalKeys'
import { Evaluate } from './evaluate/Evaluate'
import type { AiStatus } from './evaluate/AiCalls'
const FeatureGuide = lazy(() => import('./help/FeatureGuide').then(module => ({ default: module.FeatureGuide })))
const Untangle = lazy(() => import('./deck/Untangle').then(module => ({ default: module.Untangle })))

type Owner = { id: string; kind: 'org' | 'user'; watched: boolean }
type Repo = { id: string; watched: boolean; aiAllowed: boolean }
type Settings = { owners: Owner[]; repos: Repo[]; botLogins: string[]; aiBudget: number; aiVerdicts: string; aiPairs: string; aiExplain: string }
type SyncResult = { fetched: number; changed: number; calls: number; cost: number }
const queries = new QueryClient()
function SettingsPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api<Settings>('/api/settings') })
  const aiStatus = useQuery({ queryKey: ['ai'], queryFn: () => api<AiStatus>('/api/ai/status') })
  const applyPolicy = useQuery({ queryKey: ['apply-policy'], queryFn: () => api<{ enabled: boolean; sandboxRepo: string | null }>('/api/actions/policy') })
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
  return <section className="settings-page"><div className="settings-content"><div className="settings-heading"><h1>Settings</h1><p>Choose what goes on your board and what may leave your laptop.</p></div>
    <section className="settings-section" aria-labelledby="owners-heading"><h2 id="owners-heading">Watched owners</h2>
    <p>Organizations and personal accounts to search for PRs involving you. Disabling an owner pauses all its repos.</p>
    {value.owners.length === 0 && <p className="quiet">No owners added yet.</p>}
    <div className="settings-rows">{value.owners.map((o, i) => <div className="settings-row" key={o.id}>
      <div className="settings-identity"><b>{o.id}</b><small>{o.kind === 'org' ? 'Organization' : 'Personal account'}</small></div>
      <label className="settings-check"><input aria-label={`Watch owner ${o.id}`} type="checkbox" checked={o.watched}
        onChange={e => setDraft({ ...value, owners: value.owners.map((item, j) => j === i ? { ...item, watched: e.target.checked } : item) })} /> Watch</label>
    </div>)}</div>
    <form className="settings-add-form" onSubmit={e => { e.preventDefault(); setDraft({ ...value, owners: [...value.owners, { id: owner.trim(), kind, watched: true }] }); setOwner('') }}>
      <label>Owner name<input value={owner} onChange={e => setOwner(e.target.value)} required /></label>
      <label>Owner type<select value={kind} onChange={e => setKind(e.target.value === 'user' ? 'user' : 'org')}><option value="org">Organization</option><option value="user">Personal account</option></select></label>
      <button>Add owner</button>
    </form></section>
    <section className="settings-section" aria-labelledby="repos-heading"><h2 id="repos-heading">Repositories</h2><p>Watch controls board visibility and sync. Allow AI separately permits sending this repo’s metadata to Anthropic when server calls are enabled.</p>
    {value.repos.length === 0 && <p className="quiet">Repos appear after sync, or add one below.</p>}
    <div className="settings-rows">{value.repos.map((r, i) => <div className="settings-row repo-setting" key={r.id}>
      <div className="settings-identity"><b>{r.id}</b><small>{r.aiAllowed ? 'AI permission granted' : 'AI off · metadata stays local to Tangle'}</small></div>
      <div className="settings-toggles"><label className="settings-check"><input aria-label={`Watch repository ${r.id}`} type="checkbox" checked={r.watched}
        onChange={e => setDraft({ ...value, repos: value.repos.map((item, j) => j === i ? { ...item, watched: e.target.checked } : item) })} /> Watch</label>
      <label className="settings-check ai-check"><input aria-label={`Allow AI for ${r.id}`} type="checkbox" checked={r.aiAllowed}
        onChange={e => setDraft({ ...value, repos: value.repos.map((item, j) => j === i ? { ...item, aiAllowed: e.target.checked } : item) })} /> Allow AI</label></div>
    </div>)}</div>
    <form className="settings-add-form" onSubmit={e => { e.preventDefault(); setDraft({ ...value, repos: [...value.repos, { id: repo.trim(), watched: true, aiAllowed: false }] }); setRepo('') }}>
      <label>Repository (owner/repo)<input value={repo} onChange={e => setRepo(e.target.value)} required /></label><button>Add repository</button>
    </form></section>
    <section className="settings-section" aria-labelledby="privacy-heading"><h2 id="privacy-heading">AI privacy and budget</h2>
    <p>Only explicitly allowed repos may send titles, descriptions, changed paths, review excerpts and linked labels to Anthropic. Never diffs or file contents. Check client policy first.</p>
    <p className={`provider-status ${aiStatus.data?.enabled ? 'provider-enabled' : ''}`}>{aiStatus.data?.enabled ? 'Provider calls enabled on the server.' : 'Provider calls disabled on the server. Set Ai:Enabled only when ready to authorize paid API usage.'}</p>
    <label className="budget-field">Monthly AI budget (USD)<input type="number" min="0" max="100" step="0.01" value={value.aiBudget ?? 1}
      onChange={e => setDraft({ ...value, aiBudget: Number(e.target.value) })} /></label>
    <p className="quiet">API keys and GitHub tokens belong in user-secrets or your environment, never in this page.</p></section>
    <section className="settings-section" aria-labelledby="decider-heading"><h2 id="decider-heading">AI jobs</h2>
      <p>Haiku remains the default. Switching a job to Jev requires explicit server opt-in and your TYPESAFE_API_KEY.</p>
      {(['aiVerdicts', 'aiPairs', 'aiExplain'] as const).map((job, i) => <label key={job}>{['Next-step verdicts', 'Relationship suggestions', 'Written explanations'][i]}
        <select value={value[job] ?? 'haiku-5-5'} onChange={event => setDraft({ ...value, [job]: event.target.value })}>
          <option value="haiku-5-5">Haiku 5.5</option>{job !== 'aiExplain' && <option value="jev" disabled={!aiStatus.data?.jevEnabled}>Jev {!aiStatus.data?.jevEnabled && '(disabled)'}</option>}
        </select></label>)}
    </section>
    <section className="settings-section" aria-labelledby="bots-heading"><h2 id="bots-heading">Bot detection</h2><p>These accounts don’t count as human activity when calculating idle time and whose turn it is.</p>
    <label>Bot logins (comma-separated)<input value={value.botLogins.join(', ')}
      onChange={e => setDraft({ ...value, botLogins: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })} /></label>
    </section>
    <section className="settings-section" aria-labelledby="apply-policy-heading"><h2 id="apply-policy-heading">GitHub Apply safety</h2>
      <p>{applyPolicy.data?.enabled ? `Apply is restricted to sandbox: ${applyPolicy.data.sandboxRepo ?? 'none configured'}.` : 'Apply is disabled. Queuing actions never posts them.'}</p>
      <p className="quiet">Set Apply:SandboxRepo and Apply:Enabled in Host user-secrets only after authorizing sandbox tests. Each action still needs its own confirmation.</p>
      {applyPolicy.error && <p role="alert">{applyPolicy.error.message}</p>}</section>
    <PersonalKeys />
    <section className="settings-section" aria-labelledby="evaluate-settings-heading">
    <h2 id="evaluate-settings-heading">Evaluation and AI history</h2>
    <p>Review suggestion quality, compare models and inspect recent AI calls without making provider requests.</p>
    <a className="settings-evaluate-link" href="/evaluate">Open Evaluate</a>
    {aiStatus.error && <p role="alert">{aiStatus.error.message}</p>}
    </section></div>
    <footer className="settings-save"><span>Changes apply only when you save.</span><button disabled={save.isPending} onClick={() => save.mutate(value)}>{save.isPending ? 'Saving…' : 'Save settings'}</button>
    {save.error && <p role="alert">{save.error.message}</p>}{save.isSuccess && <p role="status">Settings saved.</p>}</footer>
  </section>
}
function Workspace() {
  const [path, setPath] = useState(window.location.pathname)
  const [destination, setDestination] = useState(path)
  const [switching, startTransition] = useTransition()
  const [navigationError, setNavigationError] = useState('')
  const [creatingSticky, setCreatingSticky] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const [showGuide, setShowGuide] = useState(false)
  const queryClient = useQueryClient()
  useEffect(() => {
    function navigate(next: string, updateHistory: boolean) {
      if (next === path) return
      setDestination(next)
      setNavigationError('')
      startTransition(async () => {
        try {
          await queryClient.ensureQueryData({ queryKey: ['board'], queryFn: () => api<Board>('/api/board') })
          if (next === '/untangle') {
            await Promise.all([
              import('./deck/Untangle'),
              queryClient.ensureQueryData({ queryKey: ['decisions'], queryFn: () => api<Decision[]>('/api/decisions') }),
              queryClient.ensureQueryData({ queryKey: ['actions'], queryFn: () => api<Action[]>('/api/actions') }),
              queryClient.ensureQueryData({ queryKey: ['apply-policy'], queryFn: () => api<{ enabled: boolean; sandboxRepo: string | null }>('/api/actions/policy') }),
            ])
          }
          startTransition(() => {
            if (updateHistory) window.history.pushState(null, '', next)
            setPath(next)
          })
        } catch (error) {
          setNavigationError(error instanceof Error ? error.message : 'Unable to open the next view.')
        }
      })
    }
    const click = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const anchor = event.target instanceof Element ? event.target.closest('a') : null
      if (!anchor || anchor.target || anchor.hasAttribute('download')) return
      const url = new URL(anchor.href)
      if (url.origin !== window.location.origin || url.search || url.hash || !['/', '/untangle'].includes(url.pathname)) return
      event.preventDefault()
      navigate(url.pathname, true)
    }
    const pop = () => navigate(window.location.pathname, false)
    document.addEventListener('click', click)
    window.addEventListener('popstate', pop)
    return () => { document.removeEventListener('click', click); window.removeEventListener('popstate', pop) }
  }, [path, queryClient])
  const board = useQuery({ queryKey: ['board'], queryFn: () => api<Board>('/api/board') })
  const decisions = useQuery({ queryKey: ['decisions'], queryFn: () => api<Decision[]>('/api/decisions') })
  const status = useLiveEvents()
  const dimOthers = useBoardUi(s => s.dimOthers)
  const toggleOthers = useBoardUi(s => s.toggleOthers)
  const selectCard = useBoardUi(s => s.select)
  useEffect(() => {
    const pr = new URLSearchParams(window.location.search).get('pr')
    if (path === '/' && pr) selectCard(pr)
  }, [path, selectCard])
  const attemptedAutoSync = useRef(false)
  const sync = useMutation({
    mutationFn: () => api<SyncResult>('/api/sync', { method: 'POST' }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ['board'] }); await queryClient.invalidateQueries({ queryKey: ['pr'] }); await queryClient.invalidateQueries({ queryKey: ['settings'] }) },
  })
  const signOut = useMutation({
    mutationFn: async () => {
      const response = await fetch('/api/auth/logout', { method: 'POST' })
      if (!response.ok) throw new Error('Unable to sign out.')
    },
    onSuccess: () => { queries.clear(); window.location.assign('/') },
  })
  const mutate = sync.mutate
  useEffect(() => {
    if (path !== '/' || !board.data || attemptedAutoSync.current) return
    attemptedAutoSync.current = true
    if (!board.data.lastSyncAt || Date.now() - Date.parse(board.data.lastSyncAt) > 10 * 60 * 1000) mutate()
  }, [board.data, path, mutate])
  const mine = board.data?.cards.filter(p => p.isMine).length ?? 0
  const others = (board.data?.cards.length ?? 0) - mine
  const decidedCount = activeDecisionIds(board.data?.cards ?? [], decisions.data ?? []).size
  return <><header className="top-bar"><a className="brand" href="/" aria-label="Tangle board"><svg width="30" height="30" viewBox="0 0 30 30" fill="none" aria-hidden="true"><path d="M4 22C10 4 22 4 18 14S6 16 15 11 28 14 26 24" /></svg>tangle</a>
    <nav className="view-switch"><a href="/" aria-current={path === '/' ? 'page' : undefined}>Board</a><a href="/untangle" aria-current={path === '/untangle' ? 'page' : undefined}>Untangle</a></nav>
    <div className="header-counts"><span><b>{mine}</b> yours</span><span><b>{others}</b> around you</span><span><b>{board.data?.strings.length ?? 0}</b> strings</span></div>
    <div className="header-spacer" /><span className="knot-meter">Untied {decidedCount} of {mine}<span aria-hidden="true" className="knots">{Array.from({ length: Math.min(mine, 20) }, (_, i) => <i key={i} style={{ background: i < decidedCount ? 'var(--yarn)' : undefined }} />)}</span></span>
    <button className="header-button" disabled={others === 0} aria-pressed={others > 0 && dimOthers} onClick={toggleOthers}
      title={others === 0 ? 'Every PR on this board is authored by you.' : 'Fade cards authored by other people, not cards waiting on a reviewer.'}>
      {others === 0 ? 'No others’ PRs to dim' : dimOthers ? 'Show others’ PRs' : 'Dim others’ PRs'}</button>
    {path === '/' && <button className="header-button" onClick={() => setCreatingSticky(true)}>New sticky</button>}
    <button className="header-button" disabled={sync.isPending} onClick={() => sync.mutate()}>{sync.isPending ? 'Syncing…' : 'Sync'}</button>
    <button className="header-button" onClick={() => setShowHistory(true)}>History</button>
    <a className="header-button" href="/settings">Settings</a>
    <button className="header-button" onClick={() => setShowGuide(true)}>Help</button></header>
    {showGuide && <Suspense fallback={<p className="notice" role="status">Loading feature guide…</p>}><FeatureGuide onClose={() => setShowGuide(false)} /></Suspense>}
    {path === '/settings' && <button className="sign-out-button" disabled={signOut.isPending} onClick={() => signOut.mutate()}>Lock Tangle</button>}
    {signOut.error && <p role="alert">{signOut.error.message}</p>}
    {showHistory && <ReceiptHistory onClose={() => setShowHistory(false)} />}
    {creatingSticky && <NewSticky onClose={() => setCreatingSticky(false)} />}
    {(status || sync.error) && <div className={`notice ${sync.error ? 'notice-error' : ''}`} role={sync.error ? 'alert' : 'status'}>{sync.error?.message ?? status}</div>}
    {navigationError && <div className="notice notice-error" role="alert">{navigationError}</div>}
    <div className="view-stage" aria-busy={switching}>
    {switching && <ViewLoading destination={destination} />}
    <Suspense fallback={<ViewLoading destination={destination} />}>
    {path === '/settings' ? <main className="settings-wrap"><SettingsPage /></main> :
      path === '/evaluate' ? <Evaluate /> :
      path === '/untangle' && board.data ? <Untangle board={board.data} /> :
      path === '/untangle' && board.isPending ? <ViewLoading destination="/untangle" /> :
      path === '/untangle' && board.error ? <main className="settings-wrap"><p role="alert">{board.error.message}</p></main> :
      path !== '/' ? <main className="settings-wrap"><h1>Page not found</h1><a href="/">Back to the board</a></main> :
      board.error ? <main className="settings-wrap"><p role="alert">{board.error.message}</p></main> :
      board.data ? <main className="board-workspace"><BoardView board={board.data} /><SidePanel board={board.data} />
        {board.data.cards.length === 0 && <div className="board-empty"><h1>Your yarn board</h1><p>No pull requests synced yet.</p><a href="/settings">Choose watched repositories</a></div>}</main> :
      <ViewLoading destination="/" />}
    </Suspense></div></>
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
