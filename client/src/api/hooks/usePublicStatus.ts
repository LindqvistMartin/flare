import { useQuery } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import publicApi from '@/api/publicClient'
import type {
  PublicActiveIncident,
  PublicOverallStatus,
  PublicServiceStatus,
  PublicStatusPage,
  PublicStatusWire,
} from '@/api/publicTypes'
import type { IncidentSeverity, IncidentStatus } from '@/api/types'

const VALID_OVERALL = new Set<PublicOverallStatus>(['operational', 'degraded', 'major', 'unknown'])
const VALID_SEVERITY = new Set<IncidentSeverity>(['Sev1', 'Sev2', 'Sev3', 'Sev4'])
const VALID_STATUS = new Set<IncidentStatus>([
  'Triggered', 'Investigating', 'Identified', 'Monitoring', 'Resolved', 'Closed',
])

function toOverall(status: string): PublicOverallStatus {
  return VALID_OVERALL.has(status as PublicOverallStatus)
    ? (status as PublicOverallStatus)
    : 'unknown'
}

// Wire severity/status are operator/domain strings. A backend enum addition
// (e.g. Sev5) would otherwise propagate as a typed-but-impossible value into
// SeverityBadge / StatusChip and miss the cva variant lookup. Fall back to the
// lowest-impact known value — Sev4 for severity, Investigating for status —
// so the row still renders rather than crashing the page.
function toSeverity(raw: string): IncidentSeverity {
  return VALID_SEVERITY.has(raw as IncidentSeverity) ? (raw as IncidentSeverity) : 'Sev4'
}

function toStatus(raw: string): IncidentStatus {
  return VALID_STATUS.has(raw as IncidentStatus) ? (raw as IncidentStatus) : 'Investigating'
}

function mapIncident(raw: PublicStatusWire['Services'][number]['ActiveIncidents'][number]): PublicActiveIncident {
  return {
    title: raw.Title,
    severity: toSeverity(raw.Severity),
    status: toStatus(raw.Status),
    since: raw.Since,
  }
}

function mapService(raw: PublicStatusWire['Services'][number]): PublicServiceStatus {
  return {
    name: raw.Name,
    status: toOverall(raw.Status),
    incidentsLast30Days: raw.IncidentsLast30Days,
    activeIncidents: raw.ActiveIncidents.map(mapIncident),
  }
}

function mapPage(raw: PublicStatusWire): PublicStatusPage {
  return {
    slug: raw.Slug,
    title: raw.Title,
    description: raw.Description,
    overallStatus: toOverall(raw.OverallStatus),
    generatedAt: raw.GeneratedAt,
    services: raw.Services.map(mapService),
  }
}

export function usePublicStatus(slug: string | undefined) {
  return useQuery({
    queryKey: ['public-status', slug],
    queryFn: async () => {
      const { data } = await publicApi.get<PublicStatusWire>(`/public/status/${slug}`)
      return mapPage(data)
    },
    enabled: typeof slug === 'string' && slug.length > 0,
    // Backend caches the response for 30s (StatusPageCache TTL in
    // src/Flare.Infrastructure/Caching/StatusPageCache.cs). Matching staleTime
    // avoids needless refetches when the route re-mounts.
    staleTime: 30_000,
    retry: (failureCount, error) => {
      // 404 means the slug doesn't exist — retrying never recovers.
      if (isAxiosError(error) && error.response?.status === 404) return false
      return failureCount < 2
    },
  })
}
