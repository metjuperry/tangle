import { test, expect } from '../../web/node_modules/@playwright/test'

test('personal key creation, session report and revocation', async ({ page }) => {
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
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
  await expect(page.getByText('browser-session', { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Settings', exact: true }).click()
  await page.getByRole('button', { name: 'Revoke Browser fixture' }).click()
  await expect(page.getByText('Browser fixture · Revoked')).toBeVisible()
  expect((await page.request.post('/api/sessions', { headers: { Authorization: `Bearer ${key}` }, data: report })).status()).toBe(401)
})
