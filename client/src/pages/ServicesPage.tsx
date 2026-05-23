import { useMemo, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { AppShell, type ConnectionStatus } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'
import { ServiceCard } from '@/components/ServiceCard'
import { ServiceDetailDialog } from '@/components/ServiceDetailDialog'
import { useServices } from '@/api/hooks/useServices'
import { useIncidents } from '@/api/hooks/useIncidents'
import { useFlareHub } from '@/hooks/useFlareHub'
import type { Incident, Service } from '@/api/types'

function mapConnection(state: HubConnectionState): ConnectionStatus {
  if (state === HubConnectionState.Connected) return 'connected'
  if (state === HubConnectionState.Connecting || state === HubConnectionState.Reconnecting) {
    return 'connecting'
  }
  return 'disconnected'
}

const WINDOW_DAYS = 30
const WINDOW_MS = WINDOW_DAYS * 24 * 60 * 60 * 1000

export function ServicesPage() {
  const { connectionState } = useFlareHub('dashboard')
  const { data: services, isFetched: servicesReady } = useServices()
  // Client-side derive per-service 30d history. Same shape as DashboardPage:
  // a dedicated `/api/v1/services/:id/stats` endpoint would scale better,
  // but at MVP volumes the round-trip cost dominates DB cost. Hub
  // subscription above keeps `['incidents']` warm on Created / StatusChanged.
  const { data: allIncidents } = useIncidents()

  // useState lazy-init captures Date.now() once at mount. Computing it inside
  // a useMemo body would trip react-hooks/purity since the memo can re-run.
  // Consequence: a tab left open past midnight will keep the same 30-day
  // boundary — acceptable on a portfolio MVP, fix would be a `setInterval`
  // refresh on visibility change.
  const [cutoffMs] = useState(() => Date.now() - WINDOW_MS)

  const recentByService = useMemo(() => {
    const map = new Map<string, Incident[]>()
    for (const inc of allIncidents) {
      if (new Date(inc.createdAt).getTime() < cutoffMs) continue
      const bucket = map.get(inc.serviceId) ?? []
      bucket.push(inc)
      map.set(inc.serviceId, bucket)
    }
    for (const list of map.values()) {
      list.sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    }
    return map
  }, [allIncidents, cutoffMs])

  const [open, setOpen] = useState<Service | null>(null)
  const connectionStatus = mapConnection(connectionState)

  return (
    <AppShell connectionStatus={connectionStatus}>
      <div className="space-y-6">
        <header className="flex flex-wrap items-baseline justify-between gap-3">
          <div className="space-y-1">
            <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
              Catalogue
            </p>
            <h1 className="text-lg font-semibold tracking-tight text-foreground">
              Services
            </h1>
          </div>
          <p className="font-mono text-[11px] tabular-nums text-muted-foreground">
            {services.length} registered
          </p>
        </header>

        {!servicesReady ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Card key={i} className="p-0">
                <CardContent className="space-y-2 p-4">
                  <div className="h-4 w-3/4 animate-pulse rounded-sm bg-muted" />
                  <div className="h-3 w-2/3 animate-pulse rounded-sm bg-muted" />
                  <div className="h-3 w-full animate-pulse rounded-sm bg-muted" />
                </CardContent>
              </Card>
            ))}
          </div>
        ) : services.length === 0 ? (
          <Card>
            <CardContent className="space-y-2 p-8 text-center">
              <p className="text-sm font-medium text-foreground">No services registered yet</p>
              <p className="mx-auto max-w-md font-mono text-[11px] text-muted-foreground">
                Services show up here once they're created — usually by the same automation
                that posts the first webhook into Flare.
              </p>
            </CardContent>
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {services.map(svc => (
              <ServiceCard
                key={svc.id}
                service={svc}
                incidents={recentByService.get(svc.id) ?? []}
                windowDays={WINDOW_DAYS}
                onOpen={setOpen}
              />
            ))}
          </div>
        )}
      </div>

      <ServiceDetailDialog
        service={open}
        onOpenChange={isOpen => {
          if (!isOpen) setOpen(null)
        }}
      />
    </AppShell>
  )
}
