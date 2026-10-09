import { test, expect } from '../../web/node_modules/@playwright/test'

test('local key unlocks the embedded web shell', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('heading', { name: 'Your yarn board' })).toBeVisible()
  await expect(page.getByText('No pull requests synced yet.')).toBeVisible()
})
