import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { ActionItem } from '@/api/types'

export function useActionItems(opts: { overdue?: boolean } = {}) {
  return useQuery({
    queryKey: ['action-items', opts.overdue ?? false],
    queryFn: () =>
      api
        .get<ActionItem[]>('/api/v1/action-items', {
          params: opts.overdue ? { overdue: true } : undefined,
        })
        .then(r => r.data),
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}
