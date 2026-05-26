import { test, expect } from '@playwright/test'
import { mkdirSync } from 'node:fs'

// Capture-only spec — produces docs/screenshots/*.png from a fully-seeded local
// instance (see scripts/seed-demo.ps1). Guarded by SCREENSHOT_CAPTURE=1 so the
// default `npx playwright test` does not run it (no seeded backend in CI).
//
// Run explicitly (handled by seed-demo.ps1):
//   $env:SCREENSHOT_CAPTURE='1'
//   cd client; npx playwright test playwright/tests/screenshots.capture.spec.ts --reporter=line
//
// Expected viewport: 1440x900 (dark mode). PNGs land in repo/docs/screenshots/.

test.skip(process.env['SCREENSHOT_CAPTURE'] !== '1', 'capture-only — set SCREENSHOT_CAPTURE=1 to enable')

const API = 'http://localhost:8080'
// CWD is `client/` when invoked via `cd client; npx playwright test ...`.
// docs/screenshots lives at the repo root, one level up.
const OUT = '../docs/screenshots'
mkdirSync(OUT, { recursive: true })

interface IncidentSummary {
  id: string
  title: string
  severity: string
  status: string
  createdAt: string
  commanderId: string | null
  communicatorId: string | null
  responderId: string | null
}

let activeId: string
let draftPostmortemIncidentId: string

test.use({
  viewport: { width: 1440, height: 900 },
  colorScheme: 'dark',
})

test.beforeAll(async ({ request }) => {
  const listRes = await request.get(`${API}/api/v1/incidents`)
  expect(listRes.ok(), 'GET /incidents must succeed — seed not run?').toBeTruthy()
  const list = (await listRes.json()) as IncidentSummary[]

  // Pick the active hero with all three roles assigned — its roles panel renders
  // with the assigned-id chip visible on every row (vs empty rows for partially
  // assigned ones).
  const active =
    list.find((i) => i.status === 'Investigating' && i.commanderId && i.communicatorId && i.responderId) ??
    list.find((i) => i.status === 'Investigating')
  expect(active, 'no active (Investigating) incident — seed not run?').toBeTruthy()
  activeId = active!.id

  // Find the most-recent Resolved incident that *also* has a postmortem (Draft hero).
  const resolvedSorted = list
    .filter((i) => i.status === 'Resolved')
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
  for (const inc of resolvedSorted) {
    const pmRes = await request.get(`${API}/api/v1/incidents/${inc.id}/postmortem`)
    if (pmRes.ok()) {
      draftPostmortemIncidentId = inc.id
      break
    }
  }
  expect(draftPostmortemIncidentId, 'no resolved incident with a postmortem — seed not run?').toBeTruthy()
})

// Force dark theme via the ThemeContext storage key (`fl-theme`). The app
// defaults to dark when the key is absent, but a stray prior 'light' value
// would otherwise stick — explicit setItem makes captures reproducible.
test.beforeEach(async ({ context }) => {
  await context.addInitScript(() => {
    try { window.localStorage.setItem('fl-theme', 'dark') } catch {}
  })
})

test('dashboard', async ({ page }) => {
  await page.goto('/#/dashboard')
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await expect(page.getByText('Open', { exact: true })).toBeVisible()
  // Settle TanStack queries + MTTR chart render before capturing.
  await page.waitForLoadState('networkidle')
  await page.waitForTimeout(800)
  await page.screenshot({ path: `${OUT}/dashboard.png`, fullPage: false })
})

test('incident-detail', async ({ page }) => {
  await page.goto(`/#/incidents/${activeId}`)
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  // Wait for the timeline feed to land.
  await page.waitForLoadState('networkidle')
  await page.waitForTimeout(800)
  await page.screenshot({ path: `${OUT}/incident-detail.png`, fullPage: false })
})

test('postmortem', async ({ page }) => {
  await page.goto(`/#/incidents/${draftPostmortemIncidentId}/postmortem`)
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await page.waitForLoadState('networkidle')
  await page.waitForTimeout(800)
  await page.screenshot({ path: `${OUT}/postmortem.png`, fullPage: false })
})

test('action-items', async ({ page }) => {
  await page.goto('/#/action-items')
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await page.waitForLoadState('networkidle')
  // Show the All tab (default) so the table fills the viewport. Overdue tab
  // collapses to a couple of rows on a vast empty canvas — bad first impression.
  await page.waitForTimeout(500)
  await page.screenshot({ path: `${OUT}/action-items.png`, fullPage: false })
})

test('services', async ({ page }) => {
  await page.goto('/#/services')
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await page.waitForLoadState('networkidle')
  await page.waitForTimeout(800)
  await page.screenshot({ path: `${OUT}/services.png`, fullPage: false })
})

test('cmd-k', async ({ page }) => {
  await page.goto('/#/dashboard')
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()
  await page.waitForLoadState('networkidle')
  await page.keyboard.press('ControlOrMeta+k')
  await expect(page.getByRole('dialog')).toBeVisible()
  // Wait for cmdk to mount + incident list to populate.
  await expect(page.getByPlaceholder(/Search incidents/i)).toBeVisible()
  await page.waitForTimeout(500)
  await page.screenshot({ path: `${OUT}/cmd-k.png`, fullPage: false })
})

test('public-status', async ({ page }) => {
  await page.goto('/#/p/demo')
  // Public layout does not show the FLARE wordmark.
  await expect(page.locator('h1')).toBeVisible({ timeout: 15_000 })
  await page.waitForLoadState('networkidle')
  await page.waitForTimeout(500)
  await page.screenshot({ path: `${OUT}/public-status.png`, fullPage: false })
})
