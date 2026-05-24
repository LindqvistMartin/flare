import { test, expect } from '@playwright/test'

// Smoke: ControlOrMeta+K opens the global command palette and Escape closes it.
// `ControlOrMeta` is the Playwright cross-platform alias — maps to Meta on macOS
// and Control on Linux/Windows, matching the hook in useCommandPaletteShortcut.ts.
test('command palette opens on ctrl/cmd+k and closes on escape', async ({ page }) => {
  await page.goto('/#/dashboard')

  // Wait for the dashboard mount so the global keydown listener is attached.
  await expect(page.getByText('FLARE', { exact: true })).toBeVisible()

  await page.keyboard.press('ControlOrMeta+k')

  // Radix renders Dialog as role=dialog. cmdk's Command.Input mounts inside it
  // and is matched here by placeholder rather than aria-label — Playwright's
  // accessible-name resolution against cmdk's combobox wrapper is flaky on
  // chromium, but the placeholder text comes from CommandPalette.tsx directly.
  const palette = page.getByRole('dialog')
  await expect(palette).toBeVisible()
  await expect(page.getByPlaceholder(/Search incidents/i)).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(palette).toBeHidden()
})
