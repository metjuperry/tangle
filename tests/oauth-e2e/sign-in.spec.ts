import { test, expect } from '../../web/node_modules/@playwright/test'

test('GitHub button signs into private account through fake authorization and refuses callback replay', async ({ page }) => {
  let callback = ''
  await page.context().route('https://github.com/**', route => route.abort())
  await page.route('**/auth/github/login', async route => {
    const response = await route.fetch({ maxRedirects: 0 })
    const url = new URL(response.headers().location)
    expect(url.searchParams.get('code_challenge_method')).toBe('S256')
    callback = `${url.searchParams.get('redirect_uri')}?code=alice&state=${url.searchParams.get('state')}`
    await route.fulfill({ response, headers: { ...response.headers(), location: callback } })
  })
  await page.goto('/')
  await expect(page.getByLabel('Development key', { exact: true })).toHaveCount(0)
  await page.getByRole('link', { name: 'Sign in with GitHub', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Account', exact: true })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Account', exact: true })).toContainText('alice')
  await expect(page.getByRole('region', { name: 'Account', exact: true })).toContainText('GitHub access: active')
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(page.getByRole('link', { name: 'Sign in with GitHub', exact: true })).toBeVisible()
  await page.goto(callback)
  await expect(page.getByRole('alert')).toContainText('expired or was already used')
})

test('non-allowlisted GitHub account gets friendly refusal and no session', async ({ page }) => {
  await page.context().route('https://github.com/**', route => route.abort())
  await page.route('**/auth/github/login', async route => {
    const response = await route.fetch({ maxRedirects: 0 })
    const url = new URL(response.headers().location)
    await route.fulfill({ response, headers: { ...response.headers(), location: `${url.searchParams.get('redirect_uri')}?code=denied&state=${url.searchParams.get('state')}` } })
  })
  await page.goto('/')
  await page.getByRole('link', { name: 'Sign in with GitHub', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('not on the sign-in list')
  expect((await page.request.get('/api/account')).status()).toBe(401)
})
