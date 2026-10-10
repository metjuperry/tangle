export function focusNearestCard(event: KeyboardEvent, board: HTMLElement): void {
  const directions: Record<string, [number, number]> = { ArrowRight: [1, 0], ArrowLeft: [-1, 0], ArrowDown: [0, 1], ArrowUp: [0, -1] }
  const direction = directions[event.key]
  const current = event.target instanceof HTMLElement ? event.target.closest<HTMLButtonElement>('.paper-button') : null
  if (!direction || !current || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
  const origin = current.getBoundingClientRect()
  const ox = origin.x + origin.width / 2, oy = origin.y + origin.height / 2
  let best: HTMLButtonElement | undefined, score = Infinity
  for (const button of board.querySelectorAll<HTMLButtonElement>('.paper-button')) {
    if (button === current || button.closest('.summary-hidden')) continue
    const rect = button.getBoundingClientRect()
    const dx = rect.x + rect.width / 2 - ox, dy = rect.y + rect.height / 2 - oy
    const forward = dx * direction[0] + dy * direction[1]
    if (forward <= 0) continue
    const distance = Math.hypot(dx, dy)
    if (distance < score) { best = button; score = distance }
  }
  if (best) { event.preventDefault(); best.focus() }
}
