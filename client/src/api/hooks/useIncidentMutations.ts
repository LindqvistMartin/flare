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

// All three mutations share a per-incident scope id so TanStack serialises
// concurrent .mutate() calls against the same incident. A user spamming the
// transition dropdown or rapidly clicking role assigns will not race the
// cache — each mutation runs once the previous one has settled, which keeps
// optimistic snapshots from clobbering each other.
function mutationScope(incidentId: string) {
  return { id: `incident:${incidentId}` }
}

// Optimistic transition. The mutation onSuccess writes the canonical server
// response into the cache, so the SignalR echo (which also invalidates the
// same key) lands on already-correct data and refetches harmlessly.
export function useTransitionIncident(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: mutationScope(incidentId),
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
    onSuccess: (incident) => {
      qc.setQueryData(['incident', incidentId], incident)
    },
    onError: (err, _vars, ctx) => {
      if (ctx?.previous) {
        qc.setQueryData(['incident', incidentId], ctx.previous)
      }
      toast.error(problemDetail(err, 'Transition rejected'))
    },
    onSettled: () => {
      // Dashboard list still needs to know about the new status; the
      // ['incident', id] key is already accurate via onSuccess + SignalR echo
      // so we deliberately do not double-invalidate it.
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
    scope: mutationScope(incidentId),
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

// Mirrors AllowedExternalEventTypes in src/Flare.Api/Endpoints/IncidentsEndpoints.cs.
// Severity-change UI lands in a follow-up; widening the union now avoids a
// shotgun edit when the second producer appears.
type ExternalEventType = 'CommentAdded' | 'SeverityChanged'

interface AddEventVars {
  type: ExternalEventType
  payload: string
}

export function useAddIncidentEvent(incidentId: string) {
  const qc = useQueryClient()
  return useMutation({
    scope: mutationScope(incidentId),
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
