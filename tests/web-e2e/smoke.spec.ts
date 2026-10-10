import { test, expect } from '../../web/node_modules/@playwright/test'
import { findStacks } from '../../web/src/board/stacks'
import type { Card } from '../../web/src/api/client'

test('stack detection excludes forks, cycles, cross-repo matches and batches', () => {
  const card = (id: string, headRef: string, baseRef: string, repoId = 'example/repo'): Card => ({
    id, headRef, baseRef, repoId, number: 1, title: '', author: 'viewer', isMine: true, state: 'OPEN',
    isDraft: false, createdAt: '', updatedAt: '', facts: null, batchId: null,
  })
  const root = card('one', 'first', 'main'), child = card('two', 'second', 'first')
  expect(findStacks([child, root]).map(s => s.map(c => c.id))).toEqual([['one', 'two']])
  expect(findStacks([root, child, card('three', 'third', 'first')])).toEqual([])
  expect(findStacks([root, child, card('duplicate', 'first', 'other')])).toEqual([])
  expect(findStacks([root, { ...child, repoId: 'another/repo' }])).toEqual([])
  expect(findStacks([root, { ...child, batchId: 'batch' }])).toEqual([])
  expect(findStacks([card('one', 'first', 'second'), child])).toEqual([])
})

async function unlock(page: import('../../web/node_modules/@playwright/test').Page, path = '/') {
  await page.goto(path)
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
}
test('fixture board renders cards, zones, batch and sagging strings', async ({ page }) => {
  await unlock(page)
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await expect(page.locator('.repo-zone')).toHaveCount(4)
  const zones = await page.locator('.react-flow__node-zone').evaluateAll(elements => elements.map(el => {
    const transform = new DOMMatrix(getComputedStyle(el).transform)
    return { x: transform.m41, y: transform.m42, width: el.getBoundingClientRect().width / transform.a }
  }))
  expect(new Set(zones.map(zone => zone.y)).size).toBe(1)
  expect(zones.every((zone, i) => i === 0 || zone.x > zones[i - 1].x)).toBeTruthy()
  await expect(page.locator('.batch-card')).toHaveCount(1)
  await expect(page.locator('.batch-card')).toContainText('33 scaffold applier PRs')
  await expect(page.getByRole('button', { name: 'Rule string: example/repo#49 and example/tools#195', exact: true })).toBeVisible()
  await expect(page.locator('.pr-card.aged').first()).toBeVisible()
  await expect(page.getByRole('link', { name: 'Debug PRs' })).toHaveCount(0)
})
test('card opens recorded review asks and links without editing', async ({ page }, testInfo) => {
  await unlock(page)
  await page.getByRole('button', { name: /example\/repo #49, Your turn/ }).click()
  await expect(page.getByRole('heading', { name: 'Open asks · 4' })).toBeVisible()
  await expect(page.locator('.review-ask')).toHaveCount(4)
  await expect(page.getByText('4 open review comments', { exact: true })).toBeVisible()
  await expect(page.getByRole('complementary', { name: 'Card details' }).getByRole('button', { name: /example\/tools#195/ })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Queue it' })).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('board-card-back.png'), fullPage: true })
})
test('red string tag opens connection explanation by pointer and keyboard', async ({ page }) => {
  await unlock(page)
  const tag = page.getByRole('button', { name: 'Rule string: example/repo#49 and example/tools#195' })
  await tag.click()
  await expect(page.getByRole('heading', { name: 'These belong together' })).toBeVisible()
  await page.getByRole('complementary').getByRole('button', { name: 'example/repo#49', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Open asks · 4' })).toBeVisible()
  await tag.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'These belong together' })).toBeVisible()
})
test('sync events invalidate board and open card context', async ({ page }) => {
  await unlock(page)
  await page.getByRole('button', { name: /example\/repo #49, Your turn/ }).click()
  const response = await page.request.post('/api/sync')
  expect(response.ok()).toBeTruthy()
  await expect(page.getByRole('heading', { name: 'Updated from fixture sync' })).toBeVisible()
  await expect(page.locator('[data-testid="card-example/repo#49"]')).toContainText('Updated from fixture sync')
})
test('opening stale board syncs once while fresh board does not', async ({ page }) => {
  let syncs = 0
  await page.route('**/api/board', async route => {
    const response = await route.fetch()
    const board = await response.json()
    board.lastSyncAt = '2026-01-01T00:00:00Z'
    await route.fulfill({ json: board })
  })
  page.on('request', request => { if (request.url().endsWith('/api/sync')) syncs++ })
  await unlock(page)
  await expect.poll(() => syncs).toBe(1)
  await expect(page.locator('[data-testid="card-example/repo#49"]')).toContainText('Updated from fixture sync')
  await page.unroute('**/api/board')
  await page.reload()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  expect(syncs).toBe(1)
})
test('others dim and viewport pans and zooms at 45 PR scale', async ({ page }) => {
  await unlock(page)
  await page.getByRole('button', { name: 'Dim others’ PRs' }).click()
  await expect(page.locator('.pr-card.dimmed').first()).toBeVisible()
  await expect(page.locator('.pr-card.dimmed').first()).toHaveCSS('opacity', '0.3')
  await expect(page.locator('[data-testid="card-example/repo#49"]')).toHaveCSS('opacity', '1')
  await page.getByRole('button', { name: 'Show others’ PRs' }).click()
  await expect(page.locator('.pr-card.dimmed')).toHaveCount(0)
  const viewport = page.locator('.react-flow__viewport')
  const before = await viewport.getAttribute('style')
  const board = page.getByRole('region', { name: 'Yarn board' })
  await board.hover()
  await page.mouse.wheel(0, -200)
  await expect.poll(() => viewport.getAttribute('style')).not.toBe(before)
  const bounds = await board.boundingBox()
  if (!bounds) throw new Error('Board bounds missing')
  await page.mouse.move(bounds.x + 20, bounds.y + 80)
  await page.mouse.down()
  await page.mouse.move(bounds.x + 100, bounds.y + 140, { steps: 10 })
  await page.mouse.up()
})
test('all-mine board explains why there are no other PRs to dim', async ({ page }) => {
  await page.route('**/api/board', async route => {
    const response = await route.fetch()
    const board = await response.json()
    board.cards = board.cards.filter((card: { isMine: boolean }) => card.isMine)
    await route.fulfill({ json: board })
  })
  await unlock(page)
  await expect(page.getByRole('button', { name: 'No others’ PRs to dim' })).toBeDisabled()
})
test('branch chain is ordered without internal yarn clutter and every PR opens', async ({ page }) => {
    await page.route('**/api/board', async route => {
      const response = await route.fetch()
      const board = await response.json()
      const original = board.cards.find((c: { number: number }) => c.number === 49)
      board.cards = [4, 2, 1, 3].map(number => ({ ...original, id: `example/stack#${number}`, repoId: 'example/stack',
        number, title: `Milestone ${number}`, batchId: null, headRef: `milestone/${number}`, baseRef: number === 1 ? 'main' : `milestone/${number - 1}` }))
      board.strings = [1, 2, 3].flatMap(a => [2, 3, 4].filter(b => b > a).map(b => ({
        id: `rule-${a}-${b}`, aRef: `pr:example/stack#${a}`, bRef: `pr:example/stack#${b}`, source: 'rule', rule: 'file_overlap',
        reason: 'shared files', status: 'tied'
      })))
      await route.fulfill({ json: board })
    })
    await page.route('**/api/prs/example/stack/*', route => {
      const number = Number(route.request().url().split('/').at(-1))
      return route.fulfill({ json: { body: `Description ${number}`, threads: [], events: [], strings: [] } })
    })
    await unlock(page)
    await expect(page.locator('.branch-stack')).toHaveCount(1)
    await expect(page.locator('.stack-heading')).toContainText('4 stacked PRs')
    await expect(page.locator('.stack-member .card-meta b')).toHaveText(['#1', '#2', '#3', '#4'])
    await expect(page.locator('.yarn-tag')).toHaveCount(0)
    for (const number of [1, 2, 3, 4]) {
      await page.getByRole('button', { name: new RegExp(`example/stack #${number},`) }).click()
      await expect(page.getByRole('heading', { name: `Milestone ${number}` })).toBeVisible()
    }
})
test('settings persists bot logins and removed debug path is gone', async ({ page }) => {
  await unlock(page, '/settings')
  await page.getByLabel('Bot logins (comma-separated)').fill('dependabot[bot], custom-bot')
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Settings saved.' })).toBeVisible()
  await page.reload()
  await expect(page.getByLabel('Bot logins (comma-separated)')).toHaveValue('dependabot[bot], custom-bot')
  expect((await page.request.get('/debug/prs')).status()).toBe(404)
})
