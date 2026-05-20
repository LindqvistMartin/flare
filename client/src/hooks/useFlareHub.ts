import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'

export type FlareScope =
  | { kind: 'dashboard' }
  | { kind: 'incident'; id: string }

interface FlareHubResult {
  connectionState: HubConnectionState
}

// Hub event payloads are sent as raw JSON strings from NotificationDispatcher
// (PascalCase fields IncidentId / Type / EventId), so the parsing helper below
// accepts either the legacy quoted string form or the plain-object form for
// forward compatibility.
function parsePayload(raw: unknown): Record<string, unknown> | null {
  if (raw == null) return null
  if (typeof raw === 'object') return raw as Record<string, unknown>
  if (typeof raw === 'string') {
    try {
      const parsed: unknown = JSON.parse(raw)
      return typeof parsed === 'object' && parsed !== null
        ? (parsed as Record<string, unknown>)
        : null
    } catch {
      return null
    }
  }
  return null
}

function readIncidentId(payload: Record<string, unknown> | null): string | null {
  if (!payload) return null
  const value = payload['IncidentId'] ?? payload['incidentId']
  return typeof value === 'string' ? value : null
}

export function useFlareHub(scope: FlareScope): FlareHubResult {
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const queryClient = useQueryClient()

  useEffect(() => {
    let stopped = false

    const baseUrl = (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000'
    const connection = new HubConnectionBuilder()
      .withUrl(baseUrl + '/hubs/flare')
      .withAutomaticReconnect()
      .build()

    connection.onreconnecting(() => {
      if (!stopped) setConnectionState(HubConnectionState.Reconnecting)
    })
    connection.onreconnected(() => {
      if (!stopped) setConnectionState(HubConnectionState.Connected)
    })
    connection.onclose(() => {
      if (!stopped) setConnectionState(HubConnectionState.Disconnected)
    })

    connection.on('IncidentCreated', (raw: unknown) => {
      const payload = parsePayload(raw)
      void queryClient.invalidateQueries({ queryKey: ['incidents'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
      void queryClient.invalidateQueries({ queryKey: ['mttr-by-service'] })
      void queryClient.invalidateQueries({ queryKey: ['mttr-trend', '30d'] })
      const incidentId = readIncidentId(payload)
      if (incidentId) {
        void queryClient.invalidateQueries({ queryKey: ['incident', incidentId] })
      }
    })

    connection.on('IncidentStatusChanged', (raw: unknown) => {
      const payload = parsePayload(raw)
      void queryClient.invalidateQueries({ queryKey: ['incidents'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
      void queryClient.invalidateQueries({ queryKey: ['mttr-by-service'] })
      void queryClient.invalidateQueries({ queryKey: ['mttr-trend', '30d'] })
      const incidentId = readIncidentId(payload)
      if (incidentId) {
        void queryClient.invalidateQueries({ queryKey: ['incident', incidentId] })
      }
    })

    connection.on('IncidentEventAdded', (raw: unknown) => {
      const payload = parsePayload(raw)
      const incidentId = readIncidentId(payload)
      if (incidentId) {
        void queryClient.invalidateQueries({ queryKey: ['incident-events', incidentId] })
      }
    })

    setConnectionState(HubConnectionState.Connecting)

    connection
      .start()
      .then(() => {
        if (stopped) return
        setConnectionState(HubConnectionState.Connected)
        if (scope.kind === 'dashboard') {
          return connection.invoke('JoinDashboard')
        }
        return connection.invoke('JoinIncident', scope.id)
      })
      .catch(() => {
        if (!stopped) setConnectionState(HubConnectionState.Disconnected)
      })

    return () => {
      stopped = true
      connection.off('IncidentCreated')
      connection.off('IncidentStatusChanged')
      connection.off('IncidentEventAdded')
      if (connection.state === HubConnectionState.Connected) {
        const leave =
          scope.kind === 'dashboard'
            ? connection.invoke('LeaveDashboard')
            : connection.invoke('LeaveIncident', scope.id)
        void leave.catch(() => {})
      }
      void connection.stop()
    }
  }, [scope, queryClient])

  return { connectionState }
}
