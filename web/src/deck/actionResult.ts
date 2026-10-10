const appliedLabels: Record<string, string> = {
  comment: 'Comment posted',
  request_review: 'Review requested',
  close: 'PR closed',
  merge: 'PR merged',
}
export function actionResultLabel(kind: string, status: string): string {
  if (status === 'applied') return appliedLabels[kind] ?? 'Action applied'
  if (status === 'applying') return 'Applying — check GitHub before retrying'
  if (status === 'failed') return 'Action failed or outcome uncertain'
  return status
}
export function actionResultUrl(prId: string, kind: string, result: string | null): string {
  const separator = prId.lastIndexOf('#')
  const repo = prId.slice(0, separator)
  const number = prId.slice(separator + 1)
  const fallback = `https://github.com/${repo}/pull/${number}`
  if (!result) return fallback
  let parsed: unknown
  try { parsed = JSON.parse(result) } catch { return fallback }
  const object = (value: unknown): Record<string, unknown> | undefined =>
    typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : undefined
  const operation = { comment: 'addComment', request_review: 'requestReviews', close: 'closePullRequest', merge: 'mergePullRequest' }[kind]
  const data = operation ? object(object(parsed)?.[operation]) : undefined
  const url = kind === 'comment'
    ? object(object(data?.commentEdge)?.node)?.url
    : object(data?.pullRequest)?.url
  if (typeof url !== 'string') return fallback
  // Only link to this PR's GitHub URL, never an arbitrary URL from stored text.
  return url === fallback || url.startsWith(`${fallback}#`) ? url : fallback
}
