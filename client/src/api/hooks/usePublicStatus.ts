import { useQuery } from '@tanstack/react-query'
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

function toOverall(status: string): PublicOverallStatus {
  return VALID_OVERALL.has(status as PublicOverallStatus)
    ? (status as PublicOverallStatus)
    : 'unknown'
}

function mapIncident(raw: PublicStatusWire['Services'][number]['ActiveIncidents'][number]): PublicActiveIncident {
  return {
    title: raw.Title,
    severity: raw.Severity as IncidentSeverity,
    status: raw.Status as IncidentStatus,
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
    // Backend caches the response for 30s; matching staleTime avoids needless refetches
    // when the route re-mounts (e.g. user navigates away and back).
    staleTime: 30_000,
    retry: (count, error) => {
      // Don't retry 404s — slug doesn't exist, retrying is pointless.
      const status = (error as { response?: { status?: number } }).response?.status
      if (status === 404) return false
      return count < 2
    },
  })
}
