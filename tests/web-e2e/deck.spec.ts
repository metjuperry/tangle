import { test, expect } from '../../web/node_modules/@playwright/test'

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
