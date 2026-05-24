import type { PublicOverallStatus, PublicServiceStatus } from '@/api/publicTypes'

// Maps backend OverallStatus + per-service Status values to display labels and
// tone tokens. Backend produces "operational" / "degraded" / "major" / "unknown"
// from PublicStatusEndpoints.cs:DeriveOverall / DeriveStatus; the rest of the
// alphabet (e.g. arbitrary new strings) collapses to "unknown" so a backend
// drift never lands a missing-case crash on a customer-facing route.

export type StatusTone = 'positive' | 'warning' | 'alert' | 'muted'

const OVERALL_LABELS: Record<PublicOverallStatus, string> = {
  operational: 'All systems operational',
  degraded: 'Degraded performance',
  major: 'Major outage',
  unknown: 'Status unknown',
}

const OVERALL_TONES: Record<PublicOverallStatus, StatusTone> = {
  operational: 'positive',
  degraded: 'warning',
  major: 'alert',
  unknown: 'muted',
}

const SERVICE_LABELS: Record<PublicOverallStatus, string> = {
  operational: 'Operational',
  degraded: 'Degraded',
  major: 'Outage',
  unknown: 'Unknown',
}

export function overallStatusLabel(status: PublicOverallStatus): string {
  return OVERALL_LABELS[status]
}

export function overallStatusTone(status: PublicOverallStatus): StatusTone {
  return OVERALL_TONES[status]
}

export function serviceStatusLabel(status: PublicOverallStatus): string {
  return SERVICE_LABELS[status]
}

export function serviceStatusTone(status: PublicOverallStatus): StatusTone {
  return OVERALL_TONES[status]
}

// Total count of currently-active incidents across all services on the page.
// Used in the empty-state copy decision and in subtle metadata under the header.
export function countActiveIncidents(services: PublicServiceStatus[]): number {
  let total = 0
  for (const s of services) total += s.activeIncidents.length
  return total
}
