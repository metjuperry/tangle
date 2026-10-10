export function activeDecisionIds(
  cards: readonly { id: string; isMine: boolean }[],
  decisions: readonly { prId: string }[],
): Set<string> {
  const mine = new Set(cards.filter(card => card.isMine).map(card => card.id))
  return new Set(decisions.filter(decision => mine.has(decision.prId)).map(decision => decision.prId))
}
