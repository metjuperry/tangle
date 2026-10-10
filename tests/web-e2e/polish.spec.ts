import { test, expect } from '../../web/node_modules/@playwright/test'
import AxeBuilder from '../../web/node_modules/@axe-core/playwright'
import { packLanes, stackSize } from '../../web/src/board/packing'

test('deterministic packing balances three lanes and long stacks stay three rows tall', () => {
  expect(stackSize(10)).toEqual({ width: 776, height: 538 })
  const items = Array.from({ length: 10 }, (_, i) => ({ id: `repo-${i}`, width: i === 0 ? 800 : 232, height: i === 0 ? 600 : 300 }))
  const packed = packLanes(items)
  expect([...packed]).toEqual([...packLanes([...items].reverse())])
  expect(new Set([...packed.values()].map(p => p.y)).size).toBe(3)
  for (const a of items) for (const b of items) {
    if (a.id === b.id) continue
    const p = packed.get(a.id)!, q = packed.get(b.id)!
    expect(p.x + a.width <= q.x || q.x + b.width <= p.x || p.y + a.height <= q.y || q.y + b.height <= p.y).toBeTruthy()
  }
})
test('ten-PR branch stack wraps into ordered columns without a tall single column', async ({ page }) => {
  await page.route('**/api/board', async route => {
    const response = await route.fetch()
    const board = await response.json()
    const source = board.cards[0]
    board.cards = Array.from({ length: 10 }, (_, i) => ({ ...source, id: `example/stack#${i + 1}`, repoId: 'example/stack',
      number: i + 1, title: `Milestone ${i + 1}`, batchId: null, headRef: `m-${i + 1}`, baseRef: i === 0 ? 'main' : `m-${i}` }))
    board.positions = []
    board.strings = []
    await route.fulfill({ json: board })
  })
  await open(page)
  await expect(page.locator('.stack-member')).toHaveCount(10)
  const members = await page.locator('.stack-member').evaluateAll(elements => elements.map(el => ({ x: el.getBoundingClientRect().x, y: el.getBoundingClientRect().y })))
  expect(new Set(members.map(p => Math.round(p.y))).size).toBe(3)
  expect(new Set(members.map(p => Math.round(p.x))).size).toBe(4)
  await expect(page.locator('.stack-member .card-meta b')).toHaveText(Array.from({ length: 10 }, (_, i) => `#${i + 1}`))
})

async function open(page: import('../../web/node_modules/@playwright/test').Page) {
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
}
test('search opens with Ctrl K and pans to a PR, arrows move card focus', async ({ page }) => {
  await open(page)
  await page.keyboard.press('Control+k')
  await page.getByLabel('Search titles or PR numbers').fill('#49')
  await page.getByRole('dialog').getByRole('button', { name: /example\/repo#49/ }).click()
  await expect(page.getByRole('heading', { name: 'Open asks · 4' })).toBeVisible()
  const card = page.getByRole('button', { name: /example\/repo #49,/ })
  await card.focus()
  await page.keyboard.press('ArrowRight')
  await expect(card).not.toBeFocused()
})
test('semantic zoom becomes compact then summarizes zones', async ({ page }) => {
  await open(page)
  for (let i = 0; i < 3; i++) await page.getByRole('button', { name: 'Zoom Out', exact: true }).click()
  await expect(page.locator('.compact-card').first()).toBeVisible()
  for (let i = 0; i < 4; i++) {
    const zoom = page.getByRole('button', { name: 'Zoom Out', exact: true })
    if (await zoom.isEnabled()) await zoom.click()
  }
  await expect(page.locator('.zone-summary').first()).toBeVisible()
  await page.locator('.zone-summary').first().click()
  await expect(page.locator('.zone-summary')).toHaveCount(0)
})
test('Help explains built features and closes without losing board context', async ({ page }) => {
  await open(page)
  await page.getByRole('button', { name: 'Help', exact: true }).click()
  const guide = page.getByRole('dialog', { name: 'How Tangle works' })
  await expect(guide).toBeVisible()
  await guide.getByText('Decide in Untangle', { exact: true }).click()
  await expect(guide.getByText('Snooze has no timer or automatic return.', { exact: false })).toBeVisible()
  await guide.getByText('Queue, confirm and save a round', { exact: true }).click()
  await expect(guide.getByText('It does not reset facts, notes, strings, positions or AI caching.', { exact: false })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(guide).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
})
test('axe passes board, card back and deck with reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await open(page)
  let result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
  await page.getByRole('button', { name: /example\/repo #49,/ }).click()
  await expect(page.getByRole('heading', { name: 'Open asks · 4' })).toBeVisible()
  result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
  await page.getByRole('link', { name: 'Untangle', exact: true }).click()
  await expect(page.getByRole('region', { name: 'Untangle deck' })).toBeVisible()
  await expect(page.locator('.deck-paper')).toHaveCSS('opacity', '1')
  result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
})
