import { test, expect } from '../../web/node_modules/@playwright/test'
import AxeBuilder from '../../web/node_modules/@axe-core/playwright'
import { packLanes, stackSize } from '../../web/src/board/packing'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

test('settings sitemap switches isolated sections without losing drafts and supports history and mobile', async ({ page }) => {
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  const nav = page.getByRole('navigation', { name: 'Settings sections' })
  await expect(nav.getByRole('link')).toHaveCount(8)
  await expect(nav.getByRole('link', { name: 'Watched owners', exact: true })).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('region', { name: 'Repositories', exact: true })).toBeHidden()
  await page.getByLabel('Owner name', { exact: true }).fill('unsaved-owner')
  await nav.getByRole('link', { name: 'AI privacy and budget', exact: true }).click()
  await page.getByLabel('Monthly AI budget (USD)').fill('0.75')
  await nav.getByRole('link', { name: 'Bot detection', exact: true }).click()
  await page.getByLabel('Bot logins (comma-separated)').fill('local-test-bot')
  await expect(page.getByRole('button', { name: 'Save settings', exact: true })).toBeInViewport()
  await page.goBack()
  await expect(page.getByLabel('Monthly AI budget (USD)')).toHaveValue('0.75')
  await page.goBack()
  await expect(page.getByLabel('Owner name', { exact: true })).toHaveValue('unsaved-owner')
  await nav.getByRole('link', { name: 'AI jobs', exact: true }).click()
  await page.getByRole('button', { name: 'Save settings', exact: true }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Settings saved.' })).toBeVisible()
  await page.reload()
  await expect(nav.getByRole('link', { name: 'AI jobs', exact: true })).toHaveAttribute('aria-current', 'page')
  await nav.getByRole('link', { name: 'Bot detection', exact: true }).click()
  await expect(page.getByLabel('Bot logins (comma-separated)')).toHaveValue('local-test-bot')
  await nav.getByRole('link', { name: 'AI privacy and budget', exact: true }).click()
  await expect(page.getByLabel('Monthly AI budget (USD)')).toHaveValue('0.75')
  let result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
  await page.setViewportSize({ width: 390, height: 844 })
  const picker = page.getByLabel('Settings section', { exact: true })
  await expect(picker).toBeInViewport()
  await picker.selectOption('safety')
  await expect(page.getByRole('heading', { name: 'GitHub Apply safety', exact: true })).toBeInViewport()
  await expect(page.getByRole('button', { name: 'Save settings', exact: true })).toBeInViewport()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy()
  result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
})

test('supplied branding is served unchanged and used on login and board', async ({ page, request }) => {
  const assets = ['tangle-mark.svg', 'tangle-mark-on-dark.svg', 'favicon.svg', 'favicon-16.png',
    'favicon-32.png', 'apple-touch-icon.png', 'app-icon.svg', 'app-icon-512.png']
  for (const asset of assets) {
    const response = await request.get(`/${asset}`)
    expect(response.ok()).toBeTruthy()
    expect(await response.body()).toEqual(await readFile(resolve(__dirname, '../../docs/brand', asset)))
  }
  const response = await request.get('/site.webmanifest')
  expect(response.ok()).toBeTruthy()
  expect(response.headers()['content-type']).toContain('application/manifest+json')
  const manifest = await response.json()
  expect(manifest.name).toBe('Tangle')
  expect(manifest.icons.map((icon: { src: string }) => icon.src)).toEqual(['/app-icon.svg', '/app-icon-512.png'])
  await page.goto('/')
  await expect(page.locator('link[rel="icon"]')).toHaveCount(3)
  await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute('href', '/apple-touch-icon.png')
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/site.webmanifest')
  for (const signedIn of [false, true]) {
    if (signedIn) {
      await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
      await page.getByRole('button', { name: 'Unlock Tangle' }).click()
      await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
    }
    const brand = page.locator('.brand')
    await expect(brand).toHaveText('tangle')
    await expect(brand).toHaveCSS('font-size', '24px')
    await expect(brand).toHaveCSS('font-weight', '800')
    await expect(brand).toHaveCSS('letter-spacing', '-0.48px')
    await expect(brand).toHaveCSS('font-family', /Bricolage Grotesque/)
    await expect(brand.locator('img')).toHaveAttribute('src', '/tangle-mark-on-dark.svg')
    await expect(brand.locator('img')).toHaveAttribute('alt', '')
    await expect(brand.locator('img')).toHaveCSS('width', '34px')
    await expect(brand.locator('img')).toHaveCSS('height', '34px')
    expect(await brand.locator('img').evaluate(img => (img as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
    await page.evaluate(() => document.fonts.ready)
    expect(await page.evaluate(() => [...document.fonts].some(face =>
      face.family.includes('Bricolage Grotesque') && face.weight === '800' && face.status === 'loaded'))).toBeTruthy()
  }
  await expect(page.getByRole('link', { name: 'Tangle board', exact: true })).toBeVisible()
})

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
