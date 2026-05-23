import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { toast } from 'sonner'
import api from '@/api/client'
import type { ActionItem, ActionItemStatus } from '@/api/types'

interface PatchActionItemVars {
  status: ActionItemStatus
}

interface ProblemJson {
  title?: string
  detail?: string
}

function problemDetail(err: unknown, fallback: string): string {
  if (err instanceof AxiosError) {
    const data = err.response?.data as ProblemJson | undefined
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return fallback
}

// Optimistic status toggle on a single action item. Scoped per-item so two
// rapid clicks on the same row serialise; different rows are independent.
// `status` is required — the previous nullable shape let `undefined` reach the
// wire as `{ status: null }`, which the backend would not parse as an enum.
export function useUpdateActionItemStatus(itemId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: { id: `action-item:${itemId}` },
    mutationFn: (vars: PatchActionItemVars) =>
      api
        .patch<ActionItem>(`/api/v1/action-items/${itemId}`, { status: vars.status })
        .then(r => r.data),
    onMutate: async (vars) => {
      // Snapshot every list under ['action-items', *] so we can roll back
      // both filter views together. The dashboard summary card is not
      // optimistically mutated — its rollback comes for free via the
      // invalidate-on-success path below.
      await qc.cancelQueries({ queryKey: ['action-items'] })
      const previousLists = qc.getQueriesData<ActionItem[]>({ queryKey: ['action-items'] })
      for (const [key, list] of previousLists) {
        if (!list) continue
        qc.setQueryData<ActionItem[]>(
          key,
          list.map(i => (i.id === itemId ? { ...i, status: vars.status } : i)),
        )
      }
      return { previousLists }
    },
    onError: (err, _vars, ctx) => {
      for (const [key, list] of ctx?.previousLists ?? []) {
        qc.setQueryData(key, list)
      }
      toast.error(problemDetail(err, 'Could not update action item'))
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['action-items'] })
      // Dashboard's overdue counter reads from the same dataset.
      void qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
    },
  })
}
