import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'

type AiMode = { mode: string; budget: number; hasKey: boolean; companyAvailable: boolean; local: boolean }
export function AiModeSettings() {
  const client = useQueryClient()
  const settings = useQuery({ queryKey: ['ai-mode'], queryFn: () => api<AiMode>('/api/account/ai') })
  const [mode, setMode] = useState<string | null>(null)
  const [budget, setBudget] = useState<number | null>(null)
  const [key, setKey] = useState('')
  const save = useMutation({ mutationFn: () => write('/api/account/ai', 'PUT',
    { mode: mode ?? settings.data?.mode, budget: budget ?? settings.data?.budget, key: key || null }),
  onSuccess: async () => { setKey(''); setMode(null); setBudget(null); await client.invalidateQueries({ queryKey: ['ai-mode'] }); await client.invalidateQueries({ queryKey: ['ai'] }) } })
  if (settings.error) return <p role="alert">{settings.error.message}</p>
  if (!settings.data || settings.data.local) return null
  const selected = mode ?? settings.data.mode
  return <div className="ai-mode-settings"><label>AI access<select aria-label="AI access" value={selected} onChange={event => setMode(event.target.value)}>
    <option value="off">Off</option><option value="claude_code">My Claude Code</option><option value="own_key">My Anthropic key</option>
    {settings.data.companyAvailable && <option value="company">Company key</option>}
  </select></label>
    {selected === 'own_key' && <label>Anthropic API key<input type="password" autoComplete="off" value={key} onChange={event => setKey(event.target.value)}
      placeholder={settings.data.hasKey ? 'Key stored; leave blank to keep it' : 'Enter your API key'} /></label>}
    {selected === 'claude_code' && <p>Create an Agent personal key, then run <code>claude mcp add --transport http tangle {window.location.origin}/mcp --scope user --header "Authorization: Bearer &lt;your personal key&gt;"</code> and use the pr-triage skill. Claude uses your own plan; Tangle makes no provider calls in this mode.</p>}
    <label>Your monthly AI budget (USD)<input type="number" min="0" max="100" step="0.01" value={budget ?? settings.data.budget}
      onChange={event => setBudget(Number(event.target.value))} /></label>
    <p className="quiet">The server kill switch and per-repo Allow AI switches still apply. Changing away from your key removes it. Company funding must be granted by an administrator.</p>
    <button disabled={save.isPending} onClick={() => save.mutate()}>Save AI access</button>
    {save.error && <p role="alert">{save.error.message}</p>}{save.isSuccess && <p role="status">AI access saved.</p>}
  </div>
}
