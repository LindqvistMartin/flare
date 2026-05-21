import type { IncidentStatus } from '@/api/types'

// Mirror of Incident.AllowedTransitions in src/Flare.Core/Entities/Incident.cs.
// The backend is authoritative — a forbidden transition still returns 422 — but
// surfacing only the legal next-states on the client keeps the dropdown honest
// and avoids round-trips for impossible inputs. The companion vitest verifies
// this table is internally consistent and exhaustive; **drift against the C#
// source must still be caught at PR review time** because the test cannot
// reach into another language to compare. If the matrix ever grows, surface it
// from a backend endpoint and have the FE fetch + validate at boot.
const ALLOWED_TRANSITIONS: Record<IncidentStatus, IncidentStatus[]> = {
  Triggered:     ['Investigating', 'Identified', 'Resolved'],
  Investigating: ['Identified', 'Resolved'],
  Identified:    ['Monitoring', 'Resolved'],
  Monitoring:    ['Resolved'],
  Resolved:      ['Closed'],
  Closed:        [],
}

export function allowedNextStatuses(current: IncidentStatus): IncidentStatus[] {
  return ALLOWED_TRANSITIONS[current]
}

export function isTerminalStatus(current: IncidentStatus): boolean {
  return ALLOWED_TRANSITIONS[current].length === 0
}
