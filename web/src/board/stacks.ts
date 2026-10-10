import type { Card } from '../api/client'

export function findStacks(cards: Card[]): Card[][] {
  const eligible = cards.filter(c => !c.batchId && c.headRef && c.baseRef && c.headRef !== c.baseRef)
  const parents = new Map<string, Card[]>()
  const children = new Map<string, Card[]>()
  for (const card of eligible) {
    const matches = eligible.filter(p => p.id !== card.id && p.repoId === card.repoId && p.headRef === card.baseRef)
    parents.set(card.id, matches)
    for (const parent of matches) children.set(parent.id, [...(children.get(parent.id) ?? []), card])
  }
  const stacks: Card[][] = []
  for (const root of eligible.filter(c => parents.get(c.id)?.length === 0)) {
    const chain = [root]
    const seen = new Set([root.id])
    let current = root
    let ambiguous = false
    while (true) {
      const next = children.get(current.id) ?? []
      if (next.length === 0) break
      if (next.length !== 1 || parents.get(next[0].id)?.length !== 1 || seen.has(next[0].id)) {
        ambiguous = true
        break
      }
      current = next[0]
      chain.push(current)
      seen.add(current.id)
    }
    if (!ambiguous && chain.length > 1) stacks.push(chain)
  }
  return stacks
}
