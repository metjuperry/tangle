export type AgentSession = {
  id: string
  prId: string | null
  matchedBy: string | null
  excerpt: string | null
  startedAt?: string | null
  gitBranch?: string | null
}

export function sessionUrl(id: string): string | null {
  let url: URL
  try { url = new URL(id) } catch { return null }
  if (url.protocol !== 'https:' || url.username || url.password ||
      !['claude.ai', 'code.claude.com'].includes(url.hostname)) return null
  return url.href
}

export function sessionLabel(session: AgentSession): string {
  const date = session.startedAt ? new Date(session.startedAt) : null
  if (date && !Number.isNaN(date.getTime())) return `Claude Code session · ${date.toLocaleDateString()}`
  if (session.gitBranch) return `Claude Code session · ${session.gitBranch}`
  return 'Claude Code session'
}

export function sessionMatchLabel(matchedBy: string | null): string {
  return ({ branch: 'Linked by branch', trailer: 'Linked from PR', manual: 'Attached by you' } as Record<string, string>)[matchedBy ?? ''] ?? 'Not linked yet'
}
