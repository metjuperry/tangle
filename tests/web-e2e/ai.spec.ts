import { test, expect } from '../../web/node_modules/@playwright/test'
import { actionLabel, friendlyAiText } from '../../web/src/api/aiText'

test('AI text translates known schema labels without rewriting user prose or unknown identifiers', () => {
  expect(friendlyAiText('Check `behindBy`, idleDays and review_decision.')).toBe('Check commits behind base, days without human activity and review status.')
  expect(friendlyAiText('The behindByAdapter module needs a review.')).toBe('The behindByAdapter module needs a review.')
  expect(actionLabel('fix')).toBe('Fix')
})

test('call history explains work and links to the assessed PR', async ({ page }) => {
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: true, calls: [{
    job: 'verdict', prId: 'example/repo#49', targets: '["pr:example/repo#49"]',
    outcome: 'fix: Reply to four review asks.', status: 'completed', error: null,
    createdAt: '2026-10-10T12:00:00Z', cost: .001, reservedCost: .02, inputTokens: 100, outputTokens: 40, latencyMs: 50,
  }, {
    job: 'pairs', targets: '["pr:example/repo#49 ↔ pr:example/tools#195"]',
    outcome: 'Assessed 1 candidate pairs; created 0 suggested strings.', status: 'completed', error: null,
    createdAt: '2026-10-10T12:00:00Z', cost: .001, reservedCost: .2, inputTokens: 100, outputTokens: 40, latencyMs: 50,
  }] } }))
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.locator('.top-bar').getByRole('link', { name: 'Evaluate', exact: true })).toHaveCount(0)
  await expect(page.getByText('Recent AI calls', { exact: false })).toHaveCount(0)
  await page.getByRole('link', { name: 'Open Evaluate', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Evaluate', exact: true })).toBeVisible()
  await page.getByText('Recent AI calls', { exact: false }).click()
  await expect(page.getByText('fix: Reply to four review asks.')).toBeVisible()
  await expect(page.getByText('Assessed 1 candidate pairs; created 0 suggested strings.')).toBeVisible()
  await page.getByRole('link', { name: 'example/repo#49 · View card' }).click()
  await expect(page.getByRole('heading', { name: 'Open asks · 4' })).toBeVisible()
})
test('settings save stays visible and AI history lives only in Evaluate', async ({ page }) => {
  await page.route('**/api/settings', async route => {
    if (route.request().method() !== 'GET') { await route.continue(); return }
    const response = await route.fetch()
    const settings = await response.json()
    settings.repos = Array.from({ length: 40 }, (_, i) => ({ id: `example/repo-${i}`, watched: true, aiAllowed: false }))
    await route.fulfill({ json: settings })
  })
  await page.route('**/api/ai/status', route => route.fulfill({ json: { enabled: false, calls: Array.from({ length: 50 }, (_, i) => ({
    job: 'verdict', prId: null, targets: null, outcome: `Assessment ${i}`, status: 'completed', error: null,
    createdAt: '2026-10-10T12:00:00Z', cost: .001, reservedCost: .02, inputTokens: 100, outputTokens: 40, latencyMs: 50,
  })) } }))
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  const save = page.getByRole('button', { name: 'Save settings', exact: true })
  await expect(save).toBeInViewport()
  await expect(page.locator('.ai-history')).toHaveCount(0)
  await expect(page.getByText('Assessment 0', { exact: true })).toHaveCount(0)
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(save).toBeInViewport()
  const footer = await page.locator('.settings-save').boundingBox()
  expect(footer!.y + footer!.height).toBeLessThanOrEqual(844)
  await page.locator('.settings-content').evaluate(el => { el.scrollTop = el.scrollHeight })
  await expect(save).toBeInViewport()
  await page.getByRole('link', { name: 'Open Evaluate', exact: true }).click()
  await expect(page.locator('.ai-history')).not.toHaveAttribute('open', '')
  await page.locator('.ai-history > summary').click()
  await expect(page.getByText('Assessment 0', { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Back to Settings', exact: true }).click()
  await expect(save).toBeInViewport()
})

test('AI verdict and suggested string display as proposals, tie and cut stay human actions', async ({ page }) => {
  await page.route('**/api/board', async route => {
    const response = await route.fetch()
    const board = await response.json()
    board.cards.find((c: { number: number }) => c.number === 49).verdict = {
      action: 'fix', confidence: .9, priority: 4, reason: 'Check behindBy and answer four unresolved review asks.', intent: 'Add a metadata checkpoint.',
    }
    board.strings = [{ id: 'fixture-ai', aRef: 'pr:example/repo#49', bRef: 'pr:example/tools#195',
      source: 'ai', status: 'suggested', reason: 'Both add connector metadata.', label: null }]
    await route.fulfill({ json: board })
  })
  await page.route('**/api/links/fixture-ai', route => route.fulfill({ json: { saved: true } }))
  await page.goto('/')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await page.getByRole('button', { name: /example\/repo #49,/ }).click()
  await expect(page.getByText('Check commits behind base and answer four unresolved review asks.')).toBeVisible()
  await expect(page.getByText('AI proposal, not an action.')).toBeVisible()
  await page.locator('.yarn-tag').click()
  await expect(page.getByRole('heading', { name: 'Do these belong together?' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Tie it', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Cut the string' })).toBeVisible()
})
test('repo AI switch and budget are explicit settings while server calls stay disabled', async ({ page }) => {
  await page.goto('/settings')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByText('Provider calls disabled on the server.', { exact: false })).toBeVisible()
  const row = page.locator('.repo-setting').filter({ hasText: 'example/templates' })
  await expect(row.getByRole('checkbox', { name: 'Watch repository example/templates' })).toBeChecked()
  await page.getByRole('checkbox', { name: 'Allow AI for example/templates', exact: true }).check()
  await expect(row.getByRole('checkbox', { name: 'Watch repository example/templates' })).toBeChecked()
  await expect(page.getByRole('region', { name: 'Watched owners' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Repositories' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'AI privacy and budget' })).toBeVisible()
  await page.getByLabel('Monthly AI budget (USD)').fill('0.50')
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByText('Settings saved.', { exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('checkbox', { name: 'Allow AI for example/templates', exact: true })).toBeChecked()
  await expect(page.getByLabel('Monthly AI budget (USD)')).toHaveValue('0.5')
})
