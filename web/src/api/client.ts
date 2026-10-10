export type Facts = { turn: 'me' | 'them' | 'nobody'; turnReason: string; ageDays: number; idleDays: number; behindBy: number | null; conflicting: boolean; size: string; origin: string }
export type Verdict = { action: string; confidence: number; priority: number; reason: string; intent: string; createdAt: string }
export type Card = { id: string; repoId: string; number: number; title: string; author: string | null; isMine: boolean; state: string; isDraft: boolean; createdAt: string; updatedAt: string; facts: Facts | null; batchId: string | null; headRef: string; baseRef: string; verdict?: Verdict | null }
export type Yarn = { id: string; aRef: string; bRef: string; source: 'rule' | 'ai' | 'me'; status: string; rule: string | null; reason: string | null; label: string | null }
export type Sticky = { id: string; title: string; color: string; total: number; merged: number }
export type Board = { cards: Card[]; strings: Yarn[]; cutStrings?: Yarn[]; lastSyncAt: string | null; stickies: Sticky[]; notes: { id: string; targetRef: string; text: string }[]; positions: { nodeRef: string; x: number; y: number }[] }
export type Context = { card: Card; body: string; threads: { id: string; path: string | null; lastActor: string | null; lastAt: string | null; excerpt: string | null }[]; events: { at: string; actor: string | null; kind: string }[]; strings: Yarn[] }
export const turnLabel = (turn?: string) => turn === 'me' ? 'Your turn' : turn === 'nobody' ? "Nobody’s turn" : 'Waiting on others'
export function write<T>(path: string, method: string, body: object): Promise<T> {
  return api<T>(path, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (!response.ok) {
    const body: { error?: string } = await response.json()
    throw new Error(body.error ?? `Request failed (${response.status}).`)
  }
  return response.json() as Promise<T>
}
