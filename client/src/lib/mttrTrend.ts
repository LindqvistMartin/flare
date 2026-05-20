import type { Incident } from '@/api/types'

export type MttrTrendPoint = {
  day: string
  avgMttrMs: number | null
}

// Bucket resolved incidents into 30 daily MTTR averages, indexed by the UTC date
// of `resolvedAt`. Days with no resolved incident yield `avgMttrMs: null` so the
// chart shows a gap instead of a misleading zero.
//
// Returns the series oldest-to-newest. `now` is parameterised for tests.
export function bucketIncidentsToMttrTrend(incidents: Incident[], now: Date = new Date()): MttrTrendPoint[] {
  const dayMs = 86_400_000
  const buckets = new Map<string, number[]>()

  // Pre-seed the last 30 calendar days (UTC) so the chart axis is always contiguous,
  // even on a fresh deploy with zero resolved incidents.
  for (let i = 29; i >= 0; i--) {
    const dayKey = new Date(now.getTime() - i * dayMs).toISOString().slice(0, 10)
    buckets.set(dayKey, [])
  }

  for (const incident of incidents) {
    if (!incident.resolvedAt || !incident.createdAt) continue
    const dayKey = incident.resolvedAt.slice(0, 10)
    const slot = buckets.get(dayKey)
    if (!slot) continue
    const mttrMs = new Date(incident.resolvedAt).getTime() - new Date(incident.createdAt).getTime()
    if (mttrMs < 0) continue
    slot.push(mttrMs)
  }

  return [...buckets.entries()].map(([day, values]) => ({
    day,
    avgMttrMs: values.length
      ? values.reduce((sum, v) => sum + v, 0) / values.length
      : null,
  }))
}

// Compare the most recent 7 days against the previous 23. Returns the percentage
// delta (positive = slower / worse, negative = faster / better) or null if either
// window has no resolved incidents.
export function computeMttrTrendDelta(points: MttrTrendPoint[]): number | null {
  if (points.length < 30) return null
  const recent = points.slice(-7).map(p => p.avgMttrMs).filter((v): v is number => v !== null)
  const prior = points.slice(0, 23).map(p => p.avgMttrMs).filter((v): v is number => v !== null)
  if (recent.length === 0 || prior.length === 0) return null
  const recentAvg = recent.reduce((s, v) => s + v, 0) / recent.length
  const priorAvg = prior.reduce((s, v) => s + v, 0) / prior.length
  if (priorAvg === 0) return null
  return ((recentAvg - priorAvg) / priorAvg) * 100
}
