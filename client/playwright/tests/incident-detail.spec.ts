import { test, expect } from '@playwright/test'

// Smoke: the detail route renders gracefully when the backend is offline /
// returns 404 for the stub id. Mirrors dashboard.spec.ts — backend independent.
const STUB_ID = '00000000-0000-0000-0000-000000000001'

test('incident detail renders not-found card when id is unknown', async ({ page }) => {
  await page.goto(`/#/incidents/${STUB_ID}`)

  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await expect(page.getByText('Incident not found', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: /back to dashboard/i })).toBeVisible()
})

test('detail route rejects malformed ids with the not-found surface', async ({ page }) => {
  await page.goto('/#/incidents/not-a-guid')

  await expect(page.getByText('Incident not found', { exact: true })).toBeVisible()
})
