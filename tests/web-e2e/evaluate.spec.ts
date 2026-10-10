import { test, expect } from '../../web/node_modules/@playwright/test'

test('Evaluate shows coverage, metrics and does not replay just by opening', async ({ page }) => {
  let replays = 0
  page.on('request', request => { if (request.url().includes('/api/evaluation/replay')) replays++ })
  await page.route('**/api/evaluation', route => route.fulfill({ json: {
    labelledExamples: 5, needed: 25, excludedSnoozes: 2, replayableExamples: 3, markdown: '# Tangle evaluation',
    stats: [{ decider: 'haiku-5-5', pairExamples: 3, verdictExamples: 2, precision: .5, recall: .5, agreement: .5,
      pairBrier: .43, verdictBrier: .2, costPer100: 2, latencyPer100Ms: 10000,
      disagreements: [{ target: 'example/repo#1', expected: 'fix', predicted: 'merge' }] }],
  } }))
  await page.goto('/evaluate')
  await page.getByLabel('Development key').fill('tangle-browser-test-key-not-a-secret')
  await page.getByRole('button', { name: 'Unlock Tangle' }).click()
  await expect(page.getByRole('heading', { name: 'Evaluate', exact: true })).toBeVisible()
  await expect(page.getByText('25 more needed', { exact: false })).toBeVisible()
  await expect(page.getByText('String precision')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Run evaluation replay' })).toBeDisabled()
  expect(replays).toBe(0)
  await expect(page.getByRole('link', { name: 'Download Markdown report' })).toHaveAttribute('href', '/api/evaluation/report')
})
