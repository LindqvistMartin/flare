import { test, expect } from '@playwright/test'

// Smoke: the public status route renders its own layout (no AppShell navigation)
// and shows one of the documented shells (NotFound / NetworkError / Status).
// Backend may or may not be reachable — both branches are honest UI.
test('public status route renders without app shell navigation', async ({ page }) => {
  page.on('pageerror', () => {})

  await page.goto('/#/p/anything')

  // Wait for one of the shells to land an <h1>. Backend-down realistically
  // resolves through TanStack's retry chain (~3s of 1s+2s backoff), so this
  // assertion polls long enough to outrun the chain on a slow CI runner.
  // `headingPattern` covers NotFound / NetworkError / Status (every overall
  // label).
  const headingPattern = /doesn't exist|Status unavailable|operational|degraded|outage|unknown/i
  await expect(page.locator('h1')).toHaveText(headingPattern, { timeout: 15_000 })

  await expect(page.getByText('Powered by Flare')).toBeVisible()

  // AppShell exposes the FLARE wordmark; the public layout must not.
  await expect(page.getByText('FLARE', { exact: true })).toHaveCount(0)
})

