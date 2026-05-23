import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { toast } from 'sonner'
import api from '@/api/client'
import type { Postmortem } from '@/api/types'

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

// Returns null when the backend answers 404 — the page renders an empty state
// instead of a loading spinner. Any other status surfaces as a normal error,
// which the page can show through the existing toast interceptor.
export function usePostmortem(incidentId: string | null) {
  return useQuery<Postmortem | null>({
    queryKey: ['postmortem', incidentId],
    enabled: incidentId !== null,
    queryFn: async () => {
      try {
        const res = await api.get<Postmortem>(`/api/v1/incidents/${incidentId}/postmortem`)
        return res.data
      } catch (err) {
        if (err instanceof AxiosError && err.response?.status === 404) return null
        throw err
      }
    },
  })
}

// Per-incident scope so concurrent generate + publish calls serialise.
function postmortemScope(incidentId: string) {
  return { id: `postmortem:${incidentId}` }
}

export function useGeneratePostmortem(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: postmortemScope(incidentId),
    mutationFn: () =>
      api
        .post<Postmortem>(`/api/v1/incidents/${incidentId}/postmortem/generate`)
        .then(r => r.data),
    onSuccess: (pm) => {
      qc.setQueryData(['postmortem', incidentId], pm)
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Could not generate postmortem'))
    },
  })
}

export function usePublishPostmortem(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: postmortemScope(incidentId),
    mutationFn: () =>
      api
        .post<Postmortem>(`/api/v1/incidents/${incidentId}/postmortem/publish`)
        .then(r => r.data),
    onSuccess: (pm) => {
      qc.setQueryData(['postmortem', incidentId], pm)
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Could not publish postmortem'))
    },
  })
}
