const labels: Record<string, string> = {
  behindBy: 'commits behind base',
  behind_by: 'commits behind base',
  ageDays: 'days open',
  age_days: 'days open',
  idleDays: 'days without human activity',
  idle_days: 'days without human activity',
  turnReason: 'whose-turn reason',
  turn_reason: 'whose-turn reason',
  checksState: 'CI status',
  checks_state: 'CI status',
  reviewDecision: 'review status',
  review_decision: 'review status',
  isDraft: 'draft status',
  is_draft: 'draft status',
  isMine: 'authored by you',
  is_mine: 'authored by you',
  changedFiles: 'changed files',
  changed_files: 'changed files',
  headRef: 'PR branch',
  head_ref: 'PR branch',
  baseRef: 'target branch',
  base_ref: 'target branch',
  aiAllowed: 'AI permission',
  ai_allowed: 'AI permission',
}
export function friendlyAiText(text: string): string {
  return text.replace(/`?([A-Za-z_][A-Za-z0-9_]*)`?/g, (match, key: string) => labels[key] ?? match)
}
export function actionLabel(action: string): string {
  return ({ merge: 'Merge', fix: 'Fix', ping: 'Ping', wait: 'Wait', close: 'Close', split: 'Split' } as Record<string, string>)[action] ?? action
}
