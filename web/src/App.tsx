import { useEffect, useState } from 'react'
import './App.css'

export default function App() {
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
      setKey('')
      setError('')
      setAuthenticated(true)
    } catch { setError('Unable to reach Tangle.') }
  }
  return <><header><strong>tangle</strong><span>Pull requests, untangled.</span></header>
    <main>{authenticated ? <section><h1>Your yarn board</h1><p>No pull requests synced yet.</p></section> :
      <section><h1>Open your board</h1><form onSubmit={login}>
        <label>Development key<input type="password" value={key} onChange={e => setKey(e.target.value)} required autoComplete="current-password" /></label>
        <button>Unlock Tangle</button>
      </form></section>}
      {error && <p role="alert">{error}</p>}
    </main></>
}
