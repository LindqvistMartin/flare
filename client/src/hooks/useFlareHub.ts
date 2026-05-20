import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'

export type FlareHubScope = 'dashboard' | 'incident'

interface FlareHubResult {
  connectionState: HubConnectionState
}

// NotificationDispatcher writes payload via JsonSerializer.Serialize(anon) which
// emits PascalCase. ASP.NET Core endpoints serialise camelCase via the web
// defaults, but outbox payloads go on the wire verbatim.
function parsePayload(raw: unknown): Record<string, unknown> | null {
  if (raw == null) return null
  if (typeof raw === 'object') return raw as Record<string, unknown>
  if (typeof raw !== 'string') return null
  try {
    const parsed: unknown = JSON.parse(raw)
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

function readIncidentId(payload: Record<string, unknown> | null): string | null {
  const value = payload?.['IncidentId']
  return typeof value === 'string' ? value : null
}

// Lists that must refresh when any incident is created or transitions state.
function invalidateIncidentLists(qc: QueryClient): void {
  void qc.invalidateQueries({ queryKey: ['incidents'] })
  void qc.invalidateQueries({ queryKey: ['dashboard-summary'] })
}

// Overload pair so callers can pass primitive args (no object literal that
// would re-build the effect on every render unless wrapped in useMemo).
export function useFlareHub(scope: 'dashboard'): FlareHubResult
export function useFlareHub(scope: 'incident', incidentId: string): FlareHubResult
export function useFlareHub(scope: FlareHubScope, incidentId?: string): FlareHubResult {
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const queryClient = useQueryClient()

  useEffect(() => {
    let stopped = false

    const baseUrl =
      (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000'
    const connection: HubConnection = new HubConnectionBuilder()
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
      const id = readIncidentId(payload)
      if (id === null) {
        // Producer regression — payload should always carry IncidentId. Lists
        // still refresh, but a missing id is worth logging so a backend rollback
        // does not go silent.
        console.warn('IncidentCreated payload missing IncidentId', raw)
      }
      invalidateIncidentLists(queryClient)
      // IncidentCreated cannot change the resolved-incident series, so the
      // MTTR trend deliberately does not refetch here.
      if (id !== null) {
        void queryClient.invalidateQueries({ queryKey: ['incident', id] })
      }
    })

    connection.on('IncidentStatusChanged', (raw: unknown) => {
      const payload = parsePayload(raw)
      const id = readIncidentId(payload)
      if (id === null) {
        console.warn('IncidentStatusChanged payload missing IncidentId', raw)
      }
      invalidateIncidentLists(queryClient)
      // Transition to/from Resolved shifts the MTTR series, so refetch the
      // trend on every status change rather than gambling on the `To` field.
      void queryClient.invalidateQueries({ queryKey: ['mttr-trend', '30d'] })
      if (id !== null) {
        void queryClient.invalidateQueries({ queryKey: ['incident', id] })
      }
    })

    connection.on('IncidentEventAdded', (raw: unknown) => {
      const payload = parsePayload(raw)
      const id = readIncidentId(payload)
      if (id !== null) {
        void queryClient.invalidateQueries({ queryKey: ['incident-events', id] })
      }
    })

    setConnectionState(HubConnectionState.Connecting)

    connection
      .start()
      .then(() => {
        if (stopped) return
        setConnectionState(HubConnectionState.Connected)
        if (scope === 'dashboard') {
          return connection.invoke('JoinDashboard')
        }
        if (incidentId !== undefined) {
          return connection.invoke('JoinIncident', incidentId)
        }
        return undefined
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
          scope === 'dashboard'
            ? connection.invoke('LeaveDashboard')
            : incidentId !== undefined
              ? connection.invoke('LeaveIncident', incidentId)
              : Promise.resolve()
        void leave.catch(() => {})
      }
      void connection.stop()
    }
  }, [scope, incidentId, queryClient])

  return { connectionState }
}
