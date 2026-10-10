import { test, expect } from '../../web/node_modules/@playwright/test'

test('local key unlocks the embedded web shell', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('heading', { name: 'Your yarn board' })).toBeVisible()
  await expect(page.getByText('No pull requests synced yet.')).toBeVisible()
})

test('settings persists watched owners without enabling AI', async ({ page }) => {
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByLabel('Owner name').fill('example')
  await page.getByRole('button', { name: 'Add owner' }).click()
  await page.getByLabel('Bot logins (comma-separated)').fill('dependabot[bot], custom-bot')
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByRole('status')).toHaveText('Settings saved.')
  await page.reload()
  await expect(page.getByText('example (org)')).toBeVisible()
  await expect(page.getByLabel('Bot logins (comma-separated)')).toHaveValue('dependabot[bot], custom-bot')
  await expect(page.getByText('AI remains off until M5.', { exact: false })).toBeVisible()
})

test('debug table shows deterministic facts and rule strings', async ({ page }) => {
  await page.route('**/api/board', route => route.fulfill({ json: {
    cards: [{ id: 'example/repo#49', repoId: 'example/repo', number: 49, title: 'Example', author: 'viewer', isMine: true,
      state: 'OPEN', isDraft: false, updatedAt: '2026-10-09', batchId: null,
      facts: { turn: 'me', turnReason: '4 open review comments', ageDays: 14, idleDays: 7, size: 'M', origin: 'agent' } }],
    strings: [{ id: 'rule:one', aRef: 'pr:example/repo#49', bRef: 'pr:example/repo#50', rule: 'explicit', reason: 'references #50' }]
  } }))
  await page.route('**/api/debug/prs', route => route.fulfill({ json: [{
    id: 'example/repo#49', repoId: 'example/repo', number: 49, title: 'Example', author: 'viewer', isMine: true,
    state: 'OPEN', isDraft: false, updatedAt: '2026-10-09', batchId: null,
    facts: { turn: 'me', turnReason: '4 open review comments', ageDays: 14, idleDays: 7, size: 'M', origin: 'agent' }
  }] }))
  await page.goto('/debug/prs')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('cell', { name: '4 open review comments', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'M / agent', exact: true })).toBeVisible()
  await expect(page.getByText('references #50', { exact: false })).toBeVisible()
})
