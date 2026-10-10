import { useMemo, useEffect } from 'react'
import { useQueryClient, useMutation } from '@tanstack/react-query'
import { ReactFlow, ReactFlowProvider, Handle, Position, EdgeLabelRenderer, BaseEdge, Controls, applyNodeChanges, useNodesState, useReactFlow, useStore, type Node, type NodeProps, type Edge, type EdgeProps } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { turnLabel, write, type Sticky, type Board, type Card, type Yarn } from '../api/client'
import { useBoardUi } from './store'
import { findStacks } from './stacks'
import { useTie } from './authoring'
import { Search } from './Search'
import { focusNearestCard } from './keyboard'
import { packLanes, stackSize } from './packing'

type ZoneData = { title: string; mine: number; others: number; width: number; height: number; myTurn: number; stale: number; firstCard: string }
type CardData = { card: Card; members: Card[]; stack?: boolean }
type Zone = Node<ZoneData, 'zone'>
type PrNode = Node<CardData, 'pr' | 'batch' | 'stack'>
type YarnData = { yarn: Yarn }
type YarnLink = Edge<YarnData, 'yarn'>
type StickyNodeType = Node<{ sticky: Sticky }, 'sticky'>

function ZoneNode({ data }: NodeProps<Zone>) {
  const summary = useStore(state => state.transform[2] < .35)
  const flow = useReactFlow()
  const select = useBoardUi(s => s.select)
  return <div className="repo-zone" style={{ width: data.width, height: data.height }}>
    <div className="zone-tape"><b>{data.title}</b> · {data.mine} yours{data.others > 0 && `, ${data.others} others`}</div>
    {summary && <button className="zone-summary nodrag nopan" onClick={() => { select(data.firstCard); void flow.fitView({ nodes: [{ id: `zone:${data.firstCard.split('#')[0]}` }], maxZoom: .9 }) }}>
      <strong>{data.title}</strong><span>{data.myTurn} your turn · {data.stale} stale</span><small>Zoom into this repo</small>
    </button>}
  </div>
}
function CardPaper({ data }: { data: CardData }) {
  const { card, members } = data
  const selected = useBoardUi(s => s.selection)
  const select = useBoardUi(s => s.select)
  const dim = useBoardUi(s => s.dimOthers)
  const tieFrom = useBoardUi(s => s.tieFrom)
  const tie = useTie()
  const batch = members.length > 1
  const facts = card.facts
  const turn = facts?.turn ?? 'them'
  const stamp = turn === 'me' ? 'YOUR TURN' : batch ? 'BATCH' : facts?.origin === 'bot' ? 'BOT' : turn === 'nobody' ? 'STALE' : ''
  const number = batch ? `#${Math.min(...members.map(p => p.number))}–${Math.max(...members.map(p => p.number))}` : `#${card.number}`
  const zoom = useStore(state => state.transform[2])
  return <div className={`pr-card turn-${turn} ${zoom < .6 ? 'compact-card' : ''} ${zoom < .35 ? 'summary-hidden' : ''} ${batch ? 'batch-card' : ''} ${(facts?.ageDays ?? 0) > 40 ? 'aged' : ''} ${selected === card.id ? 'selected' : ''} ${dim && members.every(p => !p.isMine) ? 'dimmed' : ''}`}
    data-testid={`card-${card.id}`}>
    <Handle type="target" position={Position.Top} id={`pin-in-${card.id}`} aria-hidden="true" tabIndex={-1} />
    <Handle type="source" position={Position.Top} id={`pin-out-${card.id}`} aria-hidden="true" tabIndex={-1} />
    <button className="pin-keyboard nodrag nopan" aria-label={`Tie a string from ${card.id}`} onClick={() => useBoardUi.getState().startTie(`pr:${card.id}`)} />
    <button className={`paper-button ${tieFrom ? 'tie-target' : ''}`} onClick={() => {
      if (tieFrom && tieFrom !== `pr:${card.id}`) tie.mutate({ aRef: tieFrom, bRef: `pr:${card.id}` })
      else select(card.id)
    }}
      aria-label={`${card.repoId} ${number}, ${turnLabel(turn)}: ${card.title}`}>
      <span className="pin" aria-hidden="true" />
      <span className="card-meta"><b>{number}</b><span>{facts?.ageDays ?? '?'}d</span></span>
      <span className="card-title">{batch ? `${members.length} ${card.title.includes('scaffold') ? 'scaffold applier' : 'related'} PRs` : card.title}</span>
      {facts?.origin === 'agent' && <span className="agent-tag">agent</span>}
      {stamp && <span className="stamp">{stamp}</span>}
    </button>
    {tie.error && <span role="alert">{tie.error.message}</span>}
  </div>
}
function PrCardNode({ data }: NodeProps<PrNode>) {
  return <CardPaper data={data} />
}
function StackNode({ data }: NodeProps<PrNode>) {
  const hidden = useStore(state => state.transform[2] < .35)
  const client = useQueryClient()
  const separate = useMutation({
    mutationFn: async () => {
      for (const [index, card] of data.members.entries())
        await write('/api/positions/' + encodeURIComponent(`pr:${card.id}`), 'PUT', { x: 24 + index % 3 * 200, y: 56 + Math.floor(index / 3) * 136 })
    },
    onSuccess: () => client.invalidateQueries({ queryKey: ['board'] }),
  })
  return <div className={`branch-stack ${hidden ? 'summary-hidden' : ''}`}>
    <Handle type="target" position={Position.Top} id="pin-in" isConnectable={false} />
    <Handle type="source" position={Position.Top} id="pin-out" isConnectable={false} />
    <div className="stack-heading">{data.members.length} stacked PRs <span>base first</span></div>
    <button className="nodrag nopan" onClick={() => separate.mutate()} disabled={separate.isPending}>Arrange separately</button>
    {separate.error && <p role="alert">{separate.error.message}</p>}
    <div className="stack-members" style={{ gridTemplateRows: `repeat(${Math.min(3, data.members.length)}, 142px)` }}>{data.members.map((card, index) => <div className="stack-member" key={card.id}>
      {index > 0 && <span className="stack-dependency" aria-label={`Depends on #${data.members[index - 1].number}`}>depends on #{data.members[index - 1].number}</span>}
      <CardPaper data={{ card, members: [card] }} />
    </div>)}</div>
  </div>
}
function StickyNode({ data }: NodeProps<StickyNodeType>) {
  const select = useBoardUi(s => s.select)
  const tieFrom = useBoardUi(s => s.tieFrom)
  const tie = useTie()
  const { sticky } = data
  return <div className={`sticky-node sticky-${sticky.color}`}>
    <Handle type="target" position={Position.Top} id="pin-in" /><Handle type="source" position={Position.Top} id="pin-out" />
    <button className="sticky-button" aria-label={`Sticky: ${sticky.title}`} onClick={() => tieFrom && tieFrom !== `sticky:${sticky.id}`
      ? tie.mutate({ aRef: tieFrom, bRef: `sticky:${sticky.id}` }) : select(`sticky:${sticky.id}`)}>
      <strong>{sticky.title}</strong><span>{sticky.merged} of {sticky.total} merged</span>
      <span className="sticky-knots" aria-hidden="true">{Array.from({ length: sticky.total }, (_, i) => <i key={i} className={i < sticky.merged ? 'filled' : ''} />)}</span>
    </button>{tie.error && <p role="alert">{tie.error.message}</p>}
  </div>
}
function YarnEdge({ sourceX, sourceY, targetX, targetY, data }: EdgeProps<YarnLink>) {
  const select = useBoardUi(s => s.selectString)
  const hidden = useStore(state => state.transform[2] < .35)
  if (!data || hidden) return null
  const distance = Math.hypot(targetX - sourceX, targetY - sourceY)
  const cx = (sourceX + targetX) / 2
  const cy = (sourceY + targetY) / 2 + 30 + distance * .12
  const path = `M${sourceX} ${sourceY} Q${cx} ${cy} ${targetX} ${targetY}`
  const mx = (sourceX + 2 * cx + targetX) / 4
  const my = (sourceY + 2 * cy + targetY) / 4
  const color = data.yarn.source === 'me' ? 'var(--mine-yarn)' : 'var(--yarn)'
  return <><BaseEdge path={path} style={{ stroke: '#28140047', strokeWidth: 5, transform: 'translate(2px, 3px)' }} />
    <BaseEdge path={path} style={{ stroke: color, strokeWidth: 3, strokeLinecap: 'round', strokeDasharray: data.yarn.status === 'suggested' ? '8 7' : undefined }} />
    <EdgeLabelRenderer><button className="yarn-tag nodrag nopan" style={{ background: color, borderColor: color, transform: `translate(-50%, -50%) translate(${mx}px, ${my}px)` }}
      aria-label={`${data.yarn.status === 'suggested' ? 'Suggested' : data.yarn.source === 'me' ? 'Your' : 'Rule'} string: ${data.yarn.aRef.replace(/^pr:/, '')} and ${data.yarn.bRef.replace(/^pr:/, '')}`}
      title="Explain this connection"
      onClick={() => select(data.yarn.id)}>{data.yarn.status === 'suggested' ? '?' : data.yarn.source === 'me' ? '✎' : '✓'}</button></EdgeLabelRenderer></>
}
const nodeTypes = { zone: ZoneNode, pr: PrCardNode, batch: PrCardNode, stack: StackNode, sticky: StickyNode }
const edgeTypes = { yarn: YarnEdge }
const rank = (p: Card) => p.facts?.turn === 'me' ? 0 : p.facts?.turn === 'nobody' ? 1 : 2

function layout(board: Board): { nodes: (Zone | PrNode | StickyNodeType)[]; edges: YarnLink[] } {
  const nodes: (Zone | PrNode | StickyNodeType)[] = []
  const mapped = new Map<string, string>()
  const repos = [...new Set(board.cards.map(p => p.repoId))].sort()
  let x = 24
  const y = 36
  for (const repo of repos) {
    const cards = board.cards.filter(p => p.repoId === repo)
    const groups = new Map<string, Card[]>()
    const stacks = findStacks(cards).filter(stack => !stack.some(c => board.positions?.some(p => p.nodeRef === `pr:${c.id}`)))
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
    const savedPositions = (board.positions ?? []).filter(p => cards.some(c => p.nodeRef === `pr:${c.id}`))
    const entryWidths = entries.map(e => e.stack ? stackSize(e.members.length).width : 176)
    const columnWidths = Array.from({ length: columns }, (_, column) =>
      Math.max(...entryWidths.filter((_, index) => index % columns === column)) + 24)
    const width = Math.max(columnWidths.reduce((sum, w) => sum + w, 0) + 32, ...savedPositions.map(p => p.x + 200))
    const rowHeights = Array.from({ length: Math.ceil(entries.length / columns) }, (_, row) =>
      Math.max(...entries.slice(row * columns, (row + 1) * columns).map(e => e.stack ? stackSize(e.members.length).height : 136)))
    const height = Math.max(rowHeights.reduce((sum, h) => sum + h, 0) + 68, ...savedPositions.map(p => p.y + 136))
    const zoneId = `zone:${repo}`
    nodes.push({ id: zoneId, type: 'zone', position: { x, y }, data: { title: repo.split('/')[1], mine: cards.filter(p => p.isMine).length, others: cards.filter(p => !p.isMine).length, width, height,
      myTurn: cards.filter(p => p.facts?.turn === 'me').length, stale: cards.filter(p => p.facts?.turn === 'nobody').length, firstCard: cards[0].id },
      style: { width, height, pointerEvents: 'all' }, selectable: false, draggable: false })
    entries.forEach(({ id, members, stack }, i) => {
      nodes.push({ id, type: stack ? 'stack' : members.length > 1 ? 'batch' : 'pr', parentId: zoneId, extent: 'parent',
        position: { x: 24 + columnWidths.slice(0, i % columns).reduce((sum, w) => sum + w, 0), y: 56 + rowHeights.slice(0, Math.floor(i / columns)).reduce((sum, h) => sum + h, 0) },
        data: { card: members[0], members, stack },
        style: { width: stack ? stackSize(members.length).width : 176, height: stack ? stackSize(members.length).height : 110, pointerEvents: 'all' }, draggable: !stack, selectable: false })
      const node = nodes[nodes.length - 1]
      const saved = board.positions?.find(p => p.nodeRef === `pr:${members[0].id}`)
      if (saved) node.position = { x: saved.x, y: saved.y }
      members.forEach(p => mapped.set(`pr:${p.id}`, id))
    })
    x += width + 36
  }
  const zones = nodes.filter((node): node is Zone => node.type === 'zone')
  const packed = packLanes(zones.map(zone => ({ id: zone.id, width: zone.data.width, height: zone.data.height })))
  zones.forEach(zone => { zone.position = packed.get(zone.id)! })
  const boardBottom = zones.reduce((bottom, zone) => Math.max(bottom, zone.position.y + zone.data.height), 36)
  for (const [index, sticky] of (board.stickies ?? []).entries()) {
    const id = `sticky:${sticky.id}`
    const saved = board.positions?.find(p => p.nodeRef === id)
    nodes.push({ id, type: 'sticky', position: saved ? { x: saved.x, y: saved.y } : { x: 30 + index * 210, y: boardBottom + 50 },
      data: { sticky }, style: { width: 180, height: 130 }, draggable: true })
    mapped.set(id, id)
  }
  const edges: YarnLink[] = []
  const handleFor = (nodeId: string, ref: string, direction: string) => {
    const node = nodes.find(n => n.id === nodeId)
    if (node?.type === 'sticky') return `pin-${direction}`
    if (!node) throw new Error('Yarn endpoint is missing.')
    const cardId = node.type === 'stack' ? ref.slice(3) : (node.data as CardData).card.id
    return `pin-${direction}-${cardId}`
  }
  for (const yarn of board.strings) {
    const source = mapped.get(yarn.aRef), target = mapped.get(yarn.bRef)
    if (!source || !target || source === target) continue
    edges.push({ id: yarn.id, type: 'yarn', source, target, sourceHandle: handleFor(source, yarn.aRef, 'out'),
      targetHandle: handleFor(target, yarn.bRef, 'in'), data: { yarn }, zIndex: 20 })
  }
  return { nodes, edges }
}
function BoardCanvas({ board }: { board: Board }) {
  const graph = useMemo(() => layout(board), [board])
  const select = useBoardUi(s => s.select)
  const tieFrom = useBoardUi(s => s.tieFrom)
  const startTie = useBoardUi(s => s.startTie)
  const tie = useTie()
  const client = useQueryClient()
  const move = useMutation({ mutationFn: ({ ref, x, y }: { ref: string; x: number; y: number }) =>
    write('/api/positions/' + encodeURIComponent(ref), 'PUT', { x, y }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['board'] }) })
  const [nodes, setNodes] = useNodesState(graph.nodes)
  const summaryZoom = useStore(state => state.transform[2] < .35)
  const visibleNodes = nodes.map<Zone | PrNode | StickyNodeType>(node => node.type === 'zone'
    ? { ...node, zIndex: summaryZoom ? 50 : 0 }
    : { ...node, style: { ...node.style, pointerEvents: summaryZoom ? 'none' : 'all', visibility: summaryZoom && node.type !== 'sticky' ? 'hidden' : 'visible' } })
  const flow = useReactFlow()
  const focusRef = useBoardUi(s => s.focusRef)
  useEffect(() => {
    if (!focusRef) return
    const node = graph.nodes.find(node => node.type !== 'zone' && 'members' in node.data &&
      node.data.members.some(card => card.id === focusRef))
    if (node) void flow.fitView({ nodes: [{ id: node.id }], padding: .5, maxZoom: 1, duration: 0 })
  }, [focusRef, graph, flow])
  // Server positions take precedence after a saved drop or a remote edit.
  useEffect(() => setNodes(graph.nodes), [graph, setNodes])
  return <section aria-label="Yarn board" className="board-cork" onKeyDown={event => focusNearestCard(event.nativeEvent, event.currentTarget)}>
    <Search board={board} />
    {tieFrom && <div className="tie-banner">Pick a card or sticky to tie {tieFrom.replace(/^pr:/, '')} to.<button onClick={() => startTie(null)}>Cancel</button></div>}
    {(tie.error || move.error) && <p role="alert" className="notice-error">{tie.error?.message ?? move.error?.message}</p>}
    <ReactFlow nodes={visibleNodes} edges={graph.edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
      fitView fitViewOptions={{ padding: .12, minZoom: .6, maxZoom: 1 }} minZoom={.2} maxZoom={1.6}
      onNodesChange={changes => setNodes(current => applyNodeChanges(changes, current))}
      onInit={instance => { void instance.fitView({ padding: .12, minZoom: .6, maxZoom: .8 }) }}
      onNodeDragStop={(_, node) => { const ref = node.type === 'sticky' ? node.id : `pr:${(node.data as CardData).card.id}`; move.mutate({ ref, x: node.position.x, y: node.position.y }) }}
      onConnect={connection => { const ref = (id: string, handle?: string | null) => id.startsWith('sticky:') ? id :
        `pr:${handle?.replace(/^pin-(?:in|out)-/, '') ?? id}`;
        tie.mutate({ aRef: ref(connection.source, connection.sourceHandle), bRef: ref(connection.target, connection.targetHandle) }) }}
      nodesConnectable elementsSelectable={false} panOnDrag zoomOnScroll
      onNodeClick={(_, node) => { if (!tieFrom && node.type === 'sticky') select(node.id); else if (!tieFrom && node.type !== 'zone' && node.type !== 'stack') select((node.data as CardData).card.id) }}
      proOptions={{ hideAttribution: false }}><Controls showInteractive={false} /></ReactFlow>
    <div className="board-legend"><span><i className="legend-pin me" />your turn</span><span><i className="legend-pin them" />waiting on others</span>
      <span><i className="legend-pin nobody" />nobody’s turn</span><span><i className="legend-yarn" />rule link · click its ✓ to read why</span><span>yellowed paper = open over 40 days</span></div>
  </section>
}
export function BoardView({ board }: { board: Board }) {
  return <ReactFlowProvider><BoardCanvas board={board} /></ReactFlowProvider>
}
