import { useMemo } from 'react'
import { ReactFlow, ReactFlowProvider, Handle, Position, EdgeLabelRenderer, BaseEdge, Controls, type Node, type NodeProps, type Edge, type EdgeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { turnLabel, type Board, type Card, type Yarn } from '../api/client'
import { useBoardUi } from './store'
import { findStacks } from './stacks'

type ZoneData = { title: string; mine: number; others: number; width: number; height: number }
type CardData = { card: Card; members: Card[]; stack?: boolean }
type Zone = Node<ZoneData, 'zone'>
type PrNode = Node<CardData, 'pr' | 'batch' | 'stack'>
type YarnData = { yarn: Yarn }
type YarnLink = Edge<YarnData, 'yarn'>

function ZoneNode({ data }: NodeProps<Zone>) {
  return <div className="repo-zone" style={{ width: data.width, height: data.height }}>
    <div className="zone-tape"><b>{data.title}</b> · {data.mine} yours{data.others > 0 && `, ${data.others} others`}</div>
  </div>
}
function CardPaper({ data }: { data: CardData }) {
  const { card, members } = data
  const selected = useBoardUi(s => s.selection)
  const select = useBoardUi(s => s.select)
  const dim = useBoardUi(s => s.dimOthers)
  const batch = members.length > 1
  const facts = card.facts
  const turn = facts?.turn ?? 'them'
  const stamp = turn === 'me' ? 'YOUR TURN' : batch ? 'BATCH' : facts?.origin === 'bot' ? 'BOT' : turn === 'nobody' ? 'STALE' : ''
  const number = batch ? `#${Math.min(...members.map(p => p.number))}–${Math.max(...members.map(p => p.number))}` : `#${card.number}`
  return <div className={`pr-card turn-${turn} ${batch ? 'batch-card' : ''} ${(facts?.ageDays ?? 0) > 40 ? 'aged' : ''} ${selected === card.id ? 'selected' : ''} ${dim && members.every(p => !p.isMine) ? 'dimmed' : ''}`}
    data-testid={`card-${card.id}`}>
    <Handle type="target" position={Position.Top} id="pin-in" isConnectable={false} />
    <Handle type="source" position={Position.Top} id="pin-out" isConnectable={false} />
    <button className="paper-button" onClick={() => select(card.id)}
      aria-label={`${card.repoId} ${number}, ${turnLabel(turn)}: ${card.title}`}>
      <span className="pin" aria-hidden="true" />
      <span className="card-meta"><b>{number}</b><span>{facts?.ageDays ?? '?'}d</span></span>
      <span className="card-title">{batch ? `${members.length} ${card.title.includes('scaffold') ? 'scaffold applier' : 'related'} PRs` : card.title}</span>
      {facts?.origin === 'agent' && <span className="agent-tag">agent</span>}
      {stamp && <span className="stamp">{stamp}</span>}
    </button>
  </div>
}
function PrCardNode({ data }: NodeProps<PrNode>) {
  return <CardPaper data={data} />
}
function StackNode({ data }: NodeProps<PrNode>) {
  return <div className="branch-stack">
    <Handle type="target" position={Position.Top} id="pin-in" isConnectable={false} />
    <Handle type="source" position={Position.Top} id="pin-out" isConnectable={false} />
    <div className="stack-heading">{data.members.length} stacked PRs <span>base first</span></div>
    <div className="stack-members">{data.members.map((card, index) => <div className="stack-member" key={card.id}>
      {index > 0 && <span className="stack-dependency" aria-label={`Depends on #${data.members[index - 1].number}`}>depends on #{data.members[index - 1].number}</span>}
      <CardPaper data={{ card, members: [card] }} />
    </div>)}</div>
  </div>
}
function YarnEdge({ sourceX, sourceY, targetX, targetY, data }: EdgeProps<YarnLink>) {
  const select = useBoardUi(s => s.selectString)
  if (!data) return null
  const distance = Math.hypot(targetX - sourceX, targetY - sourceY)
  const cx = (sourceX + targetX) / 2
  const cy = (sourceY + targetY) / 2 + 30 + distance * .12
  const path = `M${sourceX} ${sourceY} Q${cx} ${cy} ${targetX} ${targetY}`
  const mx = (sourceX + 2 * cx + targetX) / 4
  const my = (sourceY + 2 * cy + targetY) / 4
  return <><BaseEdge path={path} style={{ stroke: '#28140047', strokeWidth: 5, transform: 'translate(2px, 3px)' }} />
    <BaseEdge path={path} style={{ stroke: 'var(--yarn)', strokeWidth: 3, strokeLinecap: 'round' }} />
    <EdgeLabelRenderer><button className="yarn-tag nodrag nopan" style={{ transform: `translate(-50%, -50%) translate(${mx}px, ${my}px)` }}
      aria-label={`Rule string: ${data.yarn.aRef.slice(3)} and ${data.yarn.bRef.slice(3)}`}
      title="Explain this connection"
      onClick={() => select(data.yarn.id)}>✓</button></EdgeLabelRenderer></>
}
const nodeTypes = { zone: ZoneNode, pr: PrCardNode, batch: PrCardNode, stack: StackNode }
const edgeTypes = { yarn: YarnEdge }
const rank = (p: Card) => p.facts?.turn === 'me' ? 0 : p.facts?.turn === 'nobody' ? 1 : 2

function layout(board: Board): { nodes: (Zone | PrNode)[]; edges: YarnLink[] } {
  const nodes: (Zone | PrNode)[] = []
  const mapped = new Map<string, string>()
  const repos = [...new Set(board.cards.map(p => p.repoId))].sort()
  let x = 24, y = 36, rowHeight = 0
  for (const [index, repo] of repos.entries()) {
    const cards = board.cards.filter(p => p.repoId === repo)
    const groups = new Map<string, Card[]>()
    const stacks = findStacks(cards)
    const stackIds = new Set(stacks.map(s => `stack:${s[0].id}`))
    const stackByCard = new Map(stacks.flatMap(s => s.map(c => [c.id, `stack:${s[0].id}`] as const)))
    for (const card of cards) {
      const id = stackByCard.get(card.id) ?? card.batchId ?? card.id
      groups.set(id, [...(groups.get(id) ?? []), card])
    }
    const entries = [...groups.entries()].map(([id, members]) => ({ id, stack: stackIds.has(id),
      members: stackIds.has(id) ? stacks.find(s => `stack:${s[0].id}` === id)! :
        [...members].sort((a, b) => rank(a) - rank(b) || (b.facts?.idleDays ?? 0) - (a.facts?.idleDays ?? 0) || a.number - b.number) }))
      .sort((a, b) => rank(a.members[0]) - rank(b.members[0]) || (b.members[0].facts?.idleDays ?? 0) - (a.members[0].facts?.idleDays ?? 0) || a.members[0].number - b.members[0].number)
    const columns = Math.min(3, Math.max(1, Math.ceil(Math.sqrt(entries.length))))
    const width = columns * 200 + 32
    const rowHeights = Array.from({ length: Math.ceil(entries.length / columns) }, (_, row) =>
      Math.max(...entries.slice(row * columns, (row + 1) * columns).map(e => e.stack ? 52 + e.members.length * 142 : 136)))
    const height = rowHeights.reduce((sum, h) => sum + h, 0) + 68
    if (index > 0 && index % 2 === 0) { x = 24; y += rowHeight + 36; rowHeight = 0 }
    const zoneId = `zone:${repo}`
    nodes.push({ id: zoneId, type: 'zone', position: { x, y }, data: { title: repo.split('/')[1], mine: cards.filter(p => p.isMine).length, others: cards.filter(p => !p.isMine).length, width, height },
      style: { width, height }, selectable: false, draggable: false })
    entries.forEach(({ id, members, stack }, i) => {
      nodes.push({ id, type: stack ? 'stack' : members.length > 1 ? 'batch' : 'pr', parentId: zoneId, extent: 'parent',
        position: { x: 24 + (i % columns) * 200, y: 56 + rowHeights.slice(0, Math.floor(i / columns)).reduce((sum, h) => sum + h, 0) },
        data: { card: members[0], members, stack },
        style: { width: 176, height: stack ? 52 + members.length * 142 : 110 }, draggable: false, selectable: false })
      members.forEach(p => mapped.set(`pr:${p.id}`, id))
    })
    x += width + 36; rowHeight = Math.max(rowHeight, height)
  }
  const edges: YarnLink[] = []
  for (const yarn of board.strings) {
    const source = mapped.get(yarn.aRef), target = mapped.get(yarn.bRef)
    if (!source || !target || source === target) continue
    edges.push({ id: yarn.id, type: 'yarn', source, target, sourceHandle: 'pin-out', targetHandle: 'pin-in', data: { yarn }, zIndex: 20 })
  }
  return { nodes, edges }
}
export function BoardView({ board }: { board: Board }) {
  const graph = useMemo(() => layout(board), [board])
  const select = useBoardUi(s => s.select)
  return <section aria-label="Yarn board" className="board-cork">
    <ReactFlowProvider><ReactFlow nodes={graph.nodes} edges={graph.edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
      fitView fitViewOptions={{ padding: .12, maxZoom: 1 }} minZoom={.2} maxZoom={1.6} onlyRenderVisibleElements
      nodesDraggable={false} nodesConnectable={false} elementsSelectable={false} panOnDrag zoomOnScroll
      onNodeClick={(_, node) => { if (node.type !== 'zone' && node.type !== 'stack') select((node.data as CardData).card.id) }}
      proOptions={{ hideAttribution: false }}><Controls showInteractive={false} /></ReactFlow></ReactFlowProvider>
    <div className="board-legend"><span><i className="legend-pin me" />your turn</span><span><i className="legend-pin them" />waiting on others</span>
      <span><i className="legend-pin nobody" />nobody’s turn</span><span><i className="legend-yarn" />rule link · click its ✓ to read why</span><span>yellowed paper = open over 40 days</span></div>
  </section>
}
