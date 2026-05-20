import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { IncidentEvent } from '@/api/types'

export function useIncidentEvents(incidentId: string | null) {
  return useQuery({
    queryKey: ['incident-events', incidentId],
    queryFn: () =>
      api
        .get<IncidentEvent[]>(`/api/v1/incidents/${incidentId}/events`)
        .then(r => r.data),
    enabled: incidentId !== null,
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}
