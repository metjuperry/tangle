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
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByRole('status')).toHaveText('Settings saved.')
  await page.reload()
  await expect(page.getByText('example (org)')).toBeVisible()
  await expect(page.getByText('AI remains off until M5.', { exact: false })).toBeVisible()
})
