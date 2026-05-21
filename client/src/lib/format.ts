// Single source of truth for duration formatting on the dashboard. Returns `'—'`
// for unknown / loading values so a placeholder is visually distinct from a
// real `0` reading.
export function formatMs(ms: number | null | undefined): string {
  if (ms == null) return '—'
  if (ms === 0) return '0s'
  if (ms < 60_000) return `${Math.round(ms / 1000)}s`
  if (ms < 3_600_000) return `${Math.round(ms / 60_000)}m`
  if (ms < 86_400_000) return `${(ms / 3_600_000).toFixed(1)}h`
  return `${(ms / 86_400_000).toFixed(1)}d`
}

// Duration between two ISO timestamps. When `resolvedAt` is null the duration
// runs to now() — the caller decides whether that's meaningful.
export function formatIncidentDuration(
  openedAt: string,
  resolvedAt: string | null,
  now: Date = new Date(),
): string {
  const start = new Date(openedAt).getTime()
  const end = resolvedAt ? new Date(resolvedAt).getTime() : now.getTime()
  return formatMs(Math.max(0, end - start))
}
