import { useQuery } from '@tanstack/react-query'
import api from '@/api/client'
import type { ActionItem } from '@/api/types'

interface ActionItemQuery {
  overdue?: boolean
  postmortemId?: string
}

export function useActionItems(opts: ActionItemQuery = {}) {
  return useQuery({
    queryKey: ['action-items', opts.overdue ?? false, opts.postmortemId ?? null],
    queryFn: () => {
      const params: Record<string, unknown> = {}
      if (opts.overdue) params.overdue = true
      if (opts.postmortemId) params.postmortemId = opts.postmortemId
      return api
        .get<ActionItem[]>('/api/v1/action-items', {
          params: Object.keys(params).length > 0 ? params : undefined,
        })
        .then(r => r.data)
    },
    initialData: [],
    initialDataUpdatedAt: 0,
  })
}
