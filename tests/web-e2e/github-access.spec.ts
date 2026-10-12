import { test, expect } from '../../web/node_modules/@playwright/test'

test('expired GitHub access preserves board and disables Sync with sign-in link', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await page.route('**/api/auth/config', route => route.fulfill({ json: { mode: 'GitHub' } }))
  await page.route('**/api/account', route => route.fulfill({ json: {
    mode: 'GitHub', login: 'viewer', tokenStatus: 'invalid', syncAvailable: false, expiresAt: null,
  } }))
  await page.reload()
  await expect(page.getByRole('region', { name: 'Yarn board' })).toBeVisible()
  await expect(page.getByRole('alert')).toContainText('GitHub access expired, sign in again.')
  await expect(page.getByRole('button', { name: 'Sync', exact: true })).toBeDisabled()
  await expect(page.getByRole('link', { name: 'Sign in with GitHub', exact: true })).toHaveAttribute('href', '/auth/github/login')
})
