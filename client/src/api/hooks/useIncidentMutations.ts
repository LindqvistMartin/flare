import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { AxiosError } from 'axios'
import api from '@/api/client'
import type { Incident, IncidentEvent, IncidentStatus } from '@/api/types'

interface TransitionVars {
  to: IncidentStatus
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

// Optimistic update with rollback on 422. The dispatcher's IncidentStatusChanged
// broadcast also lands and invalidates ['incident', id]; the resulting refetch
// converges on the same canonical state the mutation's onSuccess returned,
// so the SignalR echo is a no-op rather than a flicker.
export function useTransitionIncident(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (vars: TransitionVars) =>
      api
        .post<Incident>(`/api/v1/incidents/${incidentId}/transition`, { to: vars.to })
        .then(r => r.data),
    onMutate: async (vars) => {
      await qc.cancelQueries({ queryKey: ['incident', incidentId] })
      const previous = qc.getQueryData<Incident>(['incident', incidentId])
      if (previous) {
        qc.setQueryData<Incident>(['incident', incidentId], { ...previous, status: vars.to })
      }
      return { previous }
    },
    onError: (err, _vars, ctx) => {
      if (ctx?.previous) {
        qc.setQueryData(['incident', incidentId], ctx.previous)
      }
      toast.error(problemDetail(err, 'Transition rejected'))
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ['incident', incidentId] })
      void qc.invalidateQueries({ queryKey: ['incidents'] })
    },
  })
}

interface AssignRoleVars {
  role: 'Commander' | 'Communicator' | 'Responder'
  userId: string | null
}

export function useAssignRole(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (vars: AssignRoleVars) =>
      api
        .post<Incident>(`/api/v1/incidents/${incidentId}/roles`, {
          role: vars.role,
          userId: vars.userId,
        })
        .then(r => r.data),
    onSuccess: (incident) => {
      qc.setQueryData(['incident', incidentId], incident)
      void qc.invalidateQueries({ queryKey: ['incident-events', incidentId] })
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Role assignment failed'))
    },
  })
}

interface AddEventVars {
  type: 'CommentAdded'
  payload: string
}

export function useAddIncidentEvent(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (vars: AddEventVars) =>
      api
        .post<IncidentEvent>(`/api/v1/incidents/${incidentId}/events`, vars)
        .then(r => r.data),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['incident-events', incidentId] })
      void qc.invalidateQueries({ queryKey: ['incident', incidentId] })
    },
    onError: (err) => {
      toast.error(problemDetail(err, 'Comment failed'))
    },
  })
}
