import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { Incident, IncidentFilter } from '@/api/types'

export function useIncidents(filter?: IncidentFilter) {
  return useQuery({
    queryKey: ['incidents', filter ?? null],
    queryFn: () =>
      api
        .get<Incident[]>('/api/v1/incidents', { params: filter })
        .then(r => r.data),
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}

export function useIncident(id: string | null) {
  return useQuery({
    queryKey: ['incident', id],
    queryFn: () => api.get<Incident>(`/api/v1/incidents/${id}`).then(r => r.data),
    enabled: id !== null,
  })
}
