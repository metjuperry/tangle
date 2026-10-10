import { test, expect } from '../../web/node_modules/@playwright/test'
import { sessionLabel, sessionUrl } from '../../web/src/card/sessionDisplay'

test('session labels hide raw links and accept only safe Claude URLs', () => {
  expect(sessionLabel({ id: 'https://claude.ai/code/session_123', prId: null, matchedBy: 'trailer', excerpt: null })).toBe('Claude Code session')
  expect(sessionUrl('https://claude.ai/code/session_123')).toBe('https://claude.ai/code/session_123')
  expect(sessionUrl('https://claude.ai.evil.test/session')).toBeNull()
  expect(sessionUrl('javascript:alert(1)')).toBeNull()
  expect(sessionUrl('https://user:pass@claude.ai/session')).toBeNull()
})

test('cloud session is a readable card with optional raw details', async ({ page }) => {
  const url = 'https://claude.ai/code/session_fixture'
  await page.route('**/api/sessions', route => route.fulfill({ json: [{
    id: url, prId: 'example/repo#49', matchedBy: 'trailer', excerpt: 'Plan connector validation',
  }] }))
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('button', { name: /example\/repo #49,/ }).click()
  const session = page.locator('.agent-session')
  await expect(session.getByText('Claude Code session', { exact: true })).toBeVisible()
  await expect(session.getByRole('link', { name: 'Open session' })).toHaveAttribute('href', url)
  await expect(session.getByText(url, { exact: true })).not.toBeVisible()
  await session.locator('summary').click()
  await expect(session.getByText(url, { exact: true })).toBeVisible()
})

test('personal key creation, session report and revocation', async ({ page }) => {
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('link', { name: 'Agent personal keys', exact: true }).click()
  await page.getByLabel('Personal key name').fill('Browser fixture')
  await page.getByRole('button', { name: 'Create personal key', exact: true }).click()
  const key = await page.getByLabel('New personal key — shown once').inputValue()
  expect(key).toMatch(/^tangle_/)
  const report = { sessionId: 'browser-session', transcriptPath: '/local/transcript', cwd: '/local/repo',
    gitRemote: 'https://github.com/example/repo.git', gitBranch: 'example' }
  const response = await page.request.post('/api/sessions', { headers: { Authorization: `Bearer ${key}` }, data: report })
  expect(response.ok()).toBeTruthy()
  await page.getByRole('button', { name: 'Hide key' }).click()
  await page.getByRole('link', { name: 'Board', exact: true }).click()
  await page.getByRole('button', { name: /example\/repo #49,/ }).click()
  await expect(page.getByRole('heading', { name: 'Agent sessions' })).toBeVisible()
  const reported = page.locator('.agent-session').filter({ has: page.locator('code', { hasText: 'browser-session' }) })
  await expect(reported.locator('b')).toContainText('Claude Code session')
  await reported.locator('summary').click()
  await expect(page.getByText('browser-session', { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Settings', exact: true }).click()
  await page.getByRole('link', { name: 'Agent personal keys', exact: true }).click()
  await page.getByRole('button', { name: 'Revoke Browser fixture' }).click()
  await expect(page.getByText('Browser fixture · Revoked')).toBeVisible()
  expect((await page.request.post('/api/sessions', { headers: { Authorization: `Bearer ${key}` }, data: report })).status()).toBe(401)
})
