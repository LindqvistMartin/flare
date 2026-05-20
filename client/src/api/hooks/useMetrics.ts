import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { DashboardSummary, Incident } from '@/api/types'
import { bucketIncidentsToMttrTrend, type MttrTrendPoint } from '@/lib/mttrTrend'

export function useDashboardSummary() {
  return useQuery({
    queryKey: ['dashboard-summary'],
    queryFn: () => api.get<DashboardSummary>('/api/v1/metrics/dashboard').then(r => r.data),
  })
}

// Client-side bucketing until a `/metrics/mttr/trend` endpoint ships. Fetches
// resolved incidents and folds them into a 30-day daily-average series.
//
// Backend `GET /api/v1/incidents?status=Resolved` has no `since` filter today,
// so payload size grows with the resolved-incident table. To keep refetches
// proportional, staleTime is bumped to 5 minutes and the hub deliberately
// does *not* invalidate this query on `IncidentCreated` — creation cannot
// shift a resolved-incident series, only `IncidentStatusChanged` does.
export function useMttrTrend30d() {
  return useQuery<MttrTrendPoint[]>({
    queryKey: ['mttr-trend', '30d'],
    queryFn: async () => {
      const { data } = await api.get<Incident[]>('/api/v1/incidents', {
        params: { status: 'Resolved' },
      })
      return bucketIncidentsToMttrTrend(data)
    },
    staleTime: 5 * 60_000,
  })
}
