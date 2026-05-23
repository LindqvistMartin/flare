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
// and the canonical response replaces the cache on success.
export function useUpdateServiceRunbook(serviceId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: { id: `service:${serviceId}` },
    mutationFn: (vars: UpdateRunbookVars) =>
      api
        .put<Service>(`/api/v1/services/${serviceId}`, { runbookBody: vars.runbookBody })
        .then(r => r.data),
    onSuccess: (service) => {
      qc.setQueryData(['service', serviceId], service)
      void qc.invalidateQueries({ queryKey: ['services'] })
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Could not save runbook'))
    },
  })
}
