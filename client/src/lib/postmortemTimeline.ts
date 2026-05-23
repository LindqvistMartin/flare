import type { PostmortemTimelineEntry } from '@/api/types'

// Reads either `key` or `Key` from a record. Matches the case-insensitive
// payload reading convention used by lib/timeline.ts:34.
function readField(record: Record<string, unknown>, key: string): unknown {
  const target = key.toLowerCase()
  for (const [k, v] of Object.entries(record)) {
    if (k.toLowerCase() === target) return v
  }
  return undefined
}

function isFiniteDate(value: unknown): value is string {
  if (typeof value !== 'string') return false
  return Number.isFinite(new Date(value).getTime())
}

// Parses Postmortem.timeline (a JSON-serialised array shipped by
// PostmortemDraftBuilder). Performs three jobs at the boundary:
// 1. Reject non-array roots so a broken backend doesn't crash render.
// 2. Drop entries with a missing or unparseable timestamp — the row's
//    `formatDistanceToNowStrict` would throw on `Invalid Date` otherwise.
// 3. Normalize the wire PascalCase to camelCase TS so JSX stays consistent
//    with the rest of the codebase.
export function parsePostmortemTimeline(raw: string): PostmortemTimelineEntry[] {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return []
  }
  if (!Array.isArray(parsed)) return []

  const out: PostmortemTimelineEntry[] = []
  for (const item of parsed) {
    if (item === null || typeof item !== 'object') continue
    const rec = item as Record<string, unknown>
    const at = readField(rec, 'at')
    if (!isFiniteDate(at)) continue
    const type = readField(rec, 'type')
    if (typeof type !== 'string') continue
    const summary = readField(rec, 'summary')
    const actorId = readField(rec, 'actorId')
    out.push({
      at,
      type,
      actorId: typeof actorId === 'string' ? actorId : null,
      summary: typeof summary === 'string' ? summary : '',
    })
  }
  return out
}
