import { test, expect } from '@playwright/test'

// Smoke: the public status route renders its own layout (no AppShell navigation)
// and shows the "Powered by Flare" footer. Backend may or may not be reachable —
// the page tolerates network failure and renders a fallback shell either way.
test('public status route renders without app shell navigation', async ({ page }) => {
  // Suppress unhandled-rejection noise from the public hook when the backend
  // isn't running — sonner toast is fine but Playwright treats console.error
  // as test signal under some configs.
  page.on('pageerror', () => {})

  await page.goto('/#/p/anything')

  // Either the loading skeleton (backend reachable) or NetworkError / 404 shell
  // (no backend / unknown slug) — every branch renders the footer.
  await expect(page.getByText('Powered by Flare')).toBeVisible()

  // AppShell exposes the FLARE wordmark; the public layout must not.
  await expect(page.getByText('FLARE', { exact: true })).toHaveCount(0)
})
