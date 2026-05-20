import { HubConnectionState } from '@microsoft/signalr'
import { AppShell, type ConnectionStatus } from '@/components/AppShell'
import { StatCard } from '@/components/StatCard'
import { IncidentTable } from '@/components/IncidentTable'
import { MttrTrendChart } from '@/components/MttrTrendChart'
import { useIncidents } from '@/api/hooks/useIncidents'
import { useServices } from '@/api/hooks/useServices'
import { useDashboardSummary } from '@/api/hooks/useMetrics'
import { useActionItems } from '@/api/hooks/useActionItems'
import { useFlareHub } from '@/hooks/useFlareHub'
import { formatMs } from '@/lib/format'

function mapConnection(state: HubConnectionState): ConnectionStatus {
  if (state === HubConnectionState.Connected) return 'connected'
  if (state === HubConnectionState.Connecting || state === HubConnectionState.Reconnecting) {
    return 'connecting'
  }
  return 'disconnected'
}

export function DashboardPage() {
  const { connectionState } = useFlareHub('dashboard')
  const { data: incidents, isFetching: incidentsLoading } = useIncidents()
  const { data: services } = useServices()
  const { data: summary } = useDashboardSummary()
  const { data: overdue } = useActionItems({ overdue: true })

  const activeIncidents = incidents.filter(i => i.status !== 'Closed')
  const sev1Active = activeIncidents.filter(
    i => i.severity === 'Sev1' && i.status !== 'Resolved',
  ).length

  const openCount = summary?.openIncidentsCount ?? activeIncidents.length
  const overdueCount = summary?.overdueActionItemsCount ?? overdue.length
  const summaryLoaded = summary !== undefined

  return (
    <AppShell connectionStatus={mapConnection(connectionState)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-lg font-semibold tracking-tight text-foreground">Operations</h1>
          <p className="mt-0.5 font-mono text-[11px] text-muted-foreground">
            Open incidents and MTTR refresh live via SignalR · action items poll
          </p>
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            eyebrow="Open"
            value={summaryLoaded ? openCount : '—'}
            sublabel="incidents"
            tone={openCount > 0 ? 'warning' : 'positive'}
          />
          <StatCard
            eyebrow="SEV1 active"
            value={sev1Active}
            sublabel="critical"
            tone={sev1Active > 0 ? 'alert' : 'default'}
          />
          <StatCard
            eyebrow="MTTR · 30d"
            value={formatMs(summary?.mttrLast30dAvgMs)}
            sublabel="avg"
            tone="default"
          />
          <StatCard
            eyebrow="Overdue"
            value={summaryLoaded ? overdueCount : '—'}
            sublabel="action items"
            tone={overdueCount > 0 ? 'warning' : 'default'}
          />
        </div>

        <MttrTrendChart />

        <div className="space-y-3">
          <h2 className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground">
            Active incidents
          </h2>
          <IncidentTable
            incidents={activeIncidents}
            services={services}
            loading={incidentsLoading}
          />
        </div>
      </div>
    </AppShell>
  )
}
