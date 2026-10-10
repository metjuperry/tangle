export function stackSize(count: number): { width: number; height: number } {
  return { width: Math.ceil(count / 3) * 200 - 24, height: 112 + Math.min(count, 3) * 142 }
}

export function packLanes(items: readonly { id: string; width: number; height: number }[]): Map<string, { x: number; y: number }> {
  const laneCount = Math.min(3, items.length)
  const lanes = Array.from({ length: laneCount }, () => ({ width: 0, height: 0, items: [] as typeof items[number][] }))
  // Largest groups first balances lanes without depending on data fetch order.
  const ordered = [...items].sort((a, b) => b.width * b.height - a.width * a.height || a.id.localeCompare(b.id))
  for (const item of ordered) {
    const lane = lanes.reduce((best, candidate) => candidate.width < best.width ? candidate : best)
    lane.items.push(item)
    lane.width += item.width + 36
    lane.height = Math.max(lane.height, item.height)
  }
  const positions = new Map<string, { x: number; y: number }>()
  let y = 36
  for (const lane of lanes) {
    let x = 24
    for (const item of lane.items) { positions.set(item.id, { x, y }); x += item.width + 36 }
    y += lane.height + 36
  }
  return positions
}
