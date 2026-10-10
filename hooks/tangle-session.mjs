import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'

try {
  const input = JSON.parse(readFileSync(0, 'utf8'))
  const url = new URL(process.env.TANGLE_URL ?? 'http://127.0.0.1:5202')
  if (url.username || url.password || !['http:', 'https:'].includes(url.protocol) ||
      url.protocol === 'http:' && url.hostname !== '127.0.0.1') throw new Error('TANGLE_URL must use HTTPS or loopback HTTP.')
  if (!process.env.TANGLE_PERSONAL_KEY) throw new Error('Set TANGLE_PERSONAL_KEY to an active personal key.')
  if (!input.session_id || !input.cwd || !input.transcript_path) throw new Error('Session hook input is missing metadata.')
  const git = (...args) => execFileSync('git', ['-C', input.cwd, ...args], { encoding: 'utf8' }).trim()
  const report = {
    sessionId: input.session_id, transcriptPath: input.transcript_path, cwd: input.cwd,
    gitRemote: git('remote', 'get-url', 'origin'), gitBranch: git('branch', '--show-current'),
  }
  if (process.env.TANGLE_SESSION_EXCERPT === 'true') {
    for (const line of readFileSync(input.transcript_path, 'utf8').split('\n').filter(Boolean)) {
      const entry = JSON.parse(line)
      if (entry.type !== 'user') continue
      const content = entry.message?.content
      const text = typeof content === 'string' ? content : Array.isArray(content) ?
        content.filter(block => block.type === 'text').map(block => block.text).join(' ') : ''
      if (text.trim()) { report.excerpt = text.replace(/\s+/g, ' ').trim().slice(0, 300); break }
    }
  }
  const response = await fetch(new URL('/api/sessions', url), {
    method: 'POST', headers: { Authorization: `Bearer ${process.env.TANGLE_PERSONAL_KEY}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(report), signal: AbortSignal.timeout(30000), redirect: 'error',
  })
  if (!response.ok) throw new Error(`Tangle session report rejected (${response.status}): ${await response.text()}`)
} catch (error) {
  console.error(`Tangle session hook failed: ${error.message}`)
  process.exitCode = 1
}
