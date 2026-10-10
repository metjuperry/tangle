import { test, expect } from '../../web/node_modules/@playwright/test'
import { actionResultLabel, actionResultUrl } from '../../web/src/deck/actionResult'
import { activeDecisionIds } from '../../web/src/deck/progress'

test('progress counts only distinct decisions for current own PRs', () => {
  const cards = [{ id: 'open#1', isMine: true }, { id: 'other#1', isMine: false }]
  const decisions = [{ prId: 'open#1' }, { prId: 'open#1' }, { prId: 'closed#2' }, { prId: 'merged#3' }, { prId: 'other#1' }]
  expect([...activeDecisionIds(cards, decisions)]).toEqual(['open#1'])
  expect(activeDecisionIds([], decisions).size).toBe(0)
})
test('tear saves the round, resets beads and makes open PRs available again', async ({ page }) => {
  let decisionCount = 0
  page.on('request', request => { if (request.url().endsWith('/api/decisions') && request.method() === 'POST') decisionCount++ })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('button', { name: 'Wait W', exact: true })).toBeEnabled()
  const firstCard = await page.locator('.deck-ref').textContent()
  await page.getByRole('button', { name: 'Wait W', exact: true }).click()
  await expect.poll(() => decisionCount).toBe(1)
  const receipt = page.getByRole('complementary', { name: 'Queued actions' })
  await receipt.getByRole('button', { name: 'Tear and save', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('All open PRs return to Untangle')
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page.locator('.knot-meter')).not.toContainText('Untied 0 of')
  await receipt.getByRole('button', { name: 'Tear and save', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Tear and save', exact: true }).click()
  await expect(page.locator('.knot-meter')).toContainText('Untied 0 of')
  await expect(page.locator('.deck-ref')).toHaveText(firstCard!)
  await expect(receipt.locator('.saved-receipts > summary')).toContainText('Saved receipts')
  await page.reload()
  await expect(page.locator('.knot-meter')).toContainText('Untied 0 of')
  await expect(page.locator('.deck-ref')).toHaveText(firstCard!)
})

test('closed and merged PR decisions do not inflate meter or active receipt', async ({ page }) => {
  await page.route('**/api/board', async route => {
    const response = await route.fetch()
    const board = await response.json()
    board.cards = board.cards.filter((card: { id: string }) => card.id === 'example/repo#49')
    board.strings = []
    await route.fulfill({ json: board })
  })
  await page.route('**/api/decisions', route => route.fulfill({ json: [
    { id: 'one', prId: 'example/repo#49', choice: 'fix', actionId: null },
    { id: 'two', prId: 'example/repo#2', choice: 'close', actionId: 'closed' },
    { id: 'three', prId: 'example/repo#3', choice: 'merge', actionId: 'merged' },
  ] }))
  await page.route('**/api/actions', route => route.fulfill({ json: [
    { id: 'closed', prId: 'example/repo#2', kind: 'close', payload: '{}', status: 'applied', result: '{}' },
    { id: 'merged', prId: 'example/repo#3', kind: 'merge', payload: '{}', status: 'applied', result: '{}' },
    { id: 'failed', prId: 'example/repo#49', kind: 'comment', payload: '{}', status: 'failed', result: 'Check GitHub before retrying.' },
  ] }))
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.locator('.knot-meter')).toContainText('Untied 1 of 1')
  await expect(page.locator('.deck-progress')).toContainText('1 of 1 decided')
  await expect(page.getByRole('heading', { name: 'All 1 untangled' })).toBeVisible()
  const receipt = page.getByRole('complementary', { name: 'Queued actions' })
  await expect(receipt).toContainText('No queued actions.')
  await expect(receipt.getByText('PR merged', { exact: true })).not.toBeVisible()
  await expect(receipt.getByText('PR closed', { exact: true })).not.toBeVisible()
  await expect(receipt.getByText('Action failed or outcome uncertain')).toBeVisible()
  await receipt.locator('.receipt-history:not(.saved-receipts) > summary').click()
  await expect(receipt.getByText('PR merged', { exact: true })).toBeVisible()
})

test('receipt result summaries preserve action meaning and safe GitHub links', () => {
  const pr = 'metjuperry/tangle-sandbox#1'
  const url = 'https://github.com/metjuperry/tangle-sandbox/pull/1#issuecomment-6099889517'
  expect(actionResultUrl(pr, 'comment', JSON.stringify({ addComment: { commentEdge: { node: { url } } } }))).toBe(url)
  expect(actionResultLabel('comment', 'applied')).toBe('Comment posted')
  expect(actionResultLabel('merge', 'applied')).toBe('PR merged')
  expect(actionResultLabel('close', 'applied')).toBe('PR closed')
  expect(actionResultLabel('request_review', 'applied')).toBe('Review requested')
  expect(actionResultLabel('comment', 'failed')).toBe('Action failed or outcome uncertain')
  expect(actionResultUrl(pr, 'comment', 'not JSON')).toBe(url.split('#')[0])
  expect(actionResultUrl(pr, 'comment', JSON.stringify({ addComment: { commentEdge: { node: { url: 'javascript:alert(1)' } } } }))).toBe(url.split('#')[0])
})
test('applied comment shows a friendly receipt and direct comment link', async ({ page }) => {
  const url = 'https://github.com/metjuperry/tangle-sandbox/pull/1#issuecomment-6099889517'
  await page.route('**/api/actions', async route => {
    if (route.request().method() !== 'GET') { await route.continue(); return }
    await route.fulfill({ json: [{ id: 'applied-fixture', prId: 'metjuperry/tangle-sandbox#1', kind: 'comment',
      payload: '{"Text":"Test comment"}', status: 'applied',
      result: JSON.stringify({ addComment: { commentEdge: { node: { url } } } }) }] })
  })
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  const receipt = page.getByRole('complementary', { name: 'Queued actions' })
  await receipt.locator('.receipt-history:not(.saved-receipts) > summary').click()
  await expect(receipt.getByText('Comment posted', { exact: true })).toBeVisible()
  await expect(receipt.getByRole('link', { name: 'View on GitHub' })).toHaveAttribute('href', url)
  await expect(receipt).not.toContainText('addComment')
  await expect(receipt).not.toContainText('commentEdge')
})

test('Fix acknowledges still my turn without a to-do or GitHub action', async ({ page }) => {
  let queued = 0
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/actions')) queued++ })
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('button', { name: 'Fix F', exact: true })).toBeEnabled()
  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Fix', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Still my turn' })).toBeFocused()
  const initial = await (await page.request.get('/api/decisions')).json()
  const board = await (await page.request.get('/api/board')).json()
  const cardId = await page.locator('.deck-ref').textContent()
  const originalTurn = board.cards.find((c: { id: string }) => c.id === cardId).facts.turn
  await page.getByRole('button', { name: 'Still my turn' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect.poll(async () => (await (await page.request.get('/api/decisions')).json()).length).toBe(initial.length + 1)
  const decisions = await (await page.request.get('/api/decisions')).json()
  expect(decisions.at(-1)).toMatchObject({ prId: cardId, choice: 'fix', actionId: null })
  expect(queued).toBe(0)
  await page.reload()
  await expect(page.getByRole('region', { name: 'My to-do' })).toHaveCount(0)
  await expect(page.locator('.deck-ref')).not.toHaveText(cardId!)
  const after = await (await page.request.get('/api/board')).json()
  expect(after.cards.find((c: { id: string }) => c.id === cardId).facts.turn).toBe(originalTurn)
  await page.getByRole('button', { name: 'Undo last decision · U' }).click()
  await expect(page.locator('.deck-ref')).toHaveText(cardId!)
})
test('merge captures the PR version automatically without asking for a SHA', async ({ page }) => {
  let queuedPayload: { kind: string; payload: object } | undefined
  await page.route('**/api/actions', async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return }
    queuedPayload = route.request().postDataJSON()
    await route.fulfill({ json: { id: 'merge-auto-fixture', prId: 'example/repo#49', kind: 'merge',
      payload: JSON.stringify({ HeadSha: 'a'.repeat(40) }), status: 'queued', result: null } })
  })
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('button', { name: /example\/repo #49,/ }).click()
  await page.getByRole('button', { name: 'Queue a GitHub action' }).click()
  await page.getByLabel('Action kind').selectOption('merge')
  await expect(page.getByText('Tangle captures the current PR version from GitHub', { exact: false })).toBeVisible()
  await expect(page.getByLabel('Expected head SHA')).toHaveCount(0)
  await page.getByRole('button', { name: 'Queue action', exact: true }).click()
  expect(queuedPayload).toEqual({ prId: 'example/repo#49', kind: 'merge', payload: {} })
})
test('view switch keeps board underneath a yarn loader until Untangle is ready', async ({ page }) => {
  let release!: () => void
  const ready = new Promise<void>(resolve => { release = resolve })
  await page.route('**/api/actions', async route => {
    if (route.request().method() === 'GET') await ready
    await route.continue()
  })
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await page.getByRole('link', { name: 'Untangle', exact: true }).click()
  await expect(page.getByText('Gathering your threads…')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await expect(page.locator('.loading-yarn-thread')).toHaveCSS('animation-name', 'thread-unwind')
  release()
  await expect(page.getByRole('region', { name: 'Untangle deck' })).toBeVisible()
  await expect(page.locator('.view-loading-overlay')).toHaveCount(0)
  await expect(page).toHaveURL(/\/untangle$/)
  await page.getByRole('link', { name: 'Board', exact: true }).click()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await expect(page).toHaveURL(/\/$/)
  await page.goBack()
  await expect(page.getByRole('region', { name: 'Untangle deck' })).toBeVisible()
})

test('loading overlay is static with reduced motion and exposes navigation failures', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  let release!: () => void
  const ready = new Promise<void>(resolve => { release = resolve })
  await page.route('**/api/actions', async route => {
    await ready
    await route.fulfill({ status: 503, json: { error: 'Receipt unavailable.' } })
  })
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('link', { name: 'Untangle', exact: true }).click()
  await expect(page.getByText('Gathering your threads…')).toBeVisible()
  await expect(page.locator('.loading-yarn-thread')).toHaveCSS('animation-name', 'none')
  release()
  await expect(page.getByRole('alert').filter({ hasText: 'Receipt unavailable.' })).toBeVisible()
  await expect(page.locator('.view-loading-overlay')).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
})

test('Untangle keys, queue, exact confirmation, undo and full deck', async ({ page }) => {
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('region', { name: 'Untangle deck' })).toBeVisible()
  await page.keyboard.press('f')
  await page.getByRole('button', { name: 'Draft a comment instead' }).click()
  await page.getByLabel('Comment text').fill('Exact sandbox comment')
  await page.getByRole('button', { name: 'Queue action', exact: true }).click()
  await expect(page.getByRole('complementary', { name: 'Queued actions' })).toContainText('Exact sandbox comment')
  await page.getByRole('button', { name: 'Undo last decision · U' }).click()
  await expect(page.getByRole('complementary', { name: 'Queued actions' })).not.toContainText('Exact sandbox comment')
  const cards = await (await page.request.get('/api/board')).json()
  const mine = cards.cards.filter((c: { isMine: boolean }) => c.isMine).length
  for (let i = 0; i < mine; i++) {
    const response = page.waitForResponse(r => r.url().endsWith('/api/decisions') && r.request().method() === 'POST')
    await page.getByRole('button', { name: 'Wait W', exact: true }).click()
    await response
  }
  await expect(page.getByRole('heading', { name: `All ${mine} untangled` })).toBeVisible()
  await page.keyboard.press('u')
  await expect(page.getByRole('button', { name: 'Wait W', exact: true })).toBeEnabled()
})

test('receipt requires exact per-action confirmation and cancellation sends nothing', async ({ page }) => {
  let applied = 0
  await page.route('**/api/actions/policy', route => route.fulfill({ json: { enabled: true, sandboxRepo: 'example/repo' } }))
  await page.route('**/api/actions', async route => {
    if (route.request().method() !== 'GET') { await route.continue(); return }
    await route.fulfill({ json: [{ id: 'confirmation-fixture', prId: 'example/repo#49', kind: 'comment',
      payload: JSON.stringify({ Text: 'Exact text to post.\nSecond line.' }), status: 'queued', result: null }] })
  })
  await page.route('**/api/actions/confirmation-fixture/apply', route => {
    applied++; return route.fulfill({ json: { applied: true } })
  })
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('button', { name: 'Apply', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('Exact text to post.\nSecond line.')
  await page.getByRole('button', { name: 'Cancel', exact: true }).click()
  expect(applied).toBe(0)
  await page.getByRole('button', { name: 'Apply', exact: true }).click()
  await page.getByRole('button', { name: 'Confirm and apply' }).click()
  await expect.poll(() => applied).toBe(1)
})
test('reduced-motion deck remains usable', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/untangle')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('region', { name: 'Untangle deck' })).toBeVisible()
})
