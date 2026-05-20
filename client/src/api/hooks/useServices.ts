import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { Service } from '@/api/types'

export function useServices() {
  return useQuery({
    queryKey: ['services'],
    queryFn: () => api.get<Service[]>('/api/v1/services').then(r => r.data),
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}

export function useService(id: string | null) {
  return useQuery({
    queryKey: ['service', id],
    queryFn: () => api.get<Service>(`/api/v1/services/${id}`).then(r => r.data),
    enabled: id !== null,
  })
}
