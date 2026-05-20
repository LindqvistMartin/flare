import { test, expect } from '@playwright/test'

// Smoke test: the React app boots, the dashboard route renders, the FLARE
// wordmark is visible in the header, and the stat-row eyebrows are present.
// Runs against the dev server (started by playwright.config.ts) — backend is
// not required because hooks tolerate network failure.
test('dashboard route renders header wordmark and stat eyebrows', async ({ page }) => {
  await page.goto('/#/dashboard')

  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await expect(page.getByText('Open', { exact: true })).toBeVisible()
  await expect(page.getByText('SEV1 active', { exact: true })).toBeVisible()
  await expect(page.getByText('MTTR · 30d', { exact: true })).toBeVisible()
})

test('root path redirects to /dashboard', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/#\/dashboard$/)
})
