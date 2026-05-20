import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { DashboardSummary, Incident, ServiceMttrRow } from '@/api/types'
import { bucketIncidentsToMttrTrend, type MttrTrendPoint } from '@/lib/mttrTrend'

export function useDashboardSummary() {
  return useQuery({
    queryKey: ['dashboard-summary'],
    queryFn: () =>
      api.get<DashboardSummary>('/api/v1/metrics/dashboard').then(r => r.data),
  })
}

export function useMttrByService() {
  return useQuery({
    queryKey: ['mttr-by-service'],
    queryFn: () =>
      api.get<ServiceMttrRow[]>('/api/v1/metrics/mttr').then(r => r.data),
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}

// Client-side bucketing until a /metrics/mttr/trend endpoint ships. Fetches
// resolved incidents (server returns at most a few hundred per organisation
// today; revisit when the table grows past ~10k rows) and folds them into a
// 30-day daily-average series.
export function useMttrTrend30d() {
  return useQuery<MttrTrendPoint[]>({
    queryKey: ['mttr-trend', '30d'],
    queryFn: async () => {
      const { data } = await api.get<Incident[]>('/api/v1/incidents', {
        params: { status: 'Resolved' },
      })
      return bucketIncidentsToMttrTrend(data)
    },
    staleTime: 60_000,
  })
}
