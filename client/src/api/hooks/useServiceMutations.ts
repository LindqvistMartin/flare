import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { toast } from 'sonner'
import api from '@/api/client'
import type { Service } from '@/api/types'

interface UpdateRunbookVars {
  runbookBody: string
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

// Per-service scope so concurrent debounced saves serialise. Optimistic cache
// update is not used — the editor's autosave already lives in component state
// and the canonical response replaces the cache on success. `dirty` in the
// editor compares against the server-side runbookBody from setQueryData, so
// the chip flips from Unsaved → Saving → Saved without a refetch round-trip.
export function useUpdateServiceRunbook(serviceId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: { id: `service:${serviceId}` },
    mutationFn: (vars: UpdateRunbookVars) =>
      api
        .put<Service>(`/api/v1/services/${serviceId}`, { runbookBody: vars.runbookBody })
        .then(r => r.data),
    onSuccess: (service) => {
      // Patch both individual and list cache directly so the dialog's `dirty`
      // computation sees the fresh runbookBody without a refetch flicker —
      // invalidating ['services'] used to flip Saved → Unsaved → Saved as
      // the list refetch landed.
      qc.setQueryData(['service', serviceId], service)
      qc.setQueriesData<Service[]>({ queryKey: ['services'] }, list =>
        list ? list.map(s => (s.id === service.id ? service : s)) : list,
      )
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Could not save runbook'))
    },
  })
}
