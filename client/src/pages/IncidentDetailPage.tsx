import { useParams, Link } from 'react-router-dom'
import { HubConnectionState } from '@microsoft/signalr'
import { ArrowLeft } from 'lucide-react'
import { AppShell, type ConnectionStatus } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { SeverityBadge } from '@/components/SeverityBadge'
import { StatusTransitionControl } from '@/components/StatusTransitionControl'
import { RolesPanel } from '@/components/RolesPanel'
import { IncidentTimeline } from '@/components/IncidentTimeline'
import { CommentComposer } from '@/components/CommentComposer'
import { RunbookSidebar } from '@/components/RunbookSidebar'
import { useIncident } from '@/api/hooks/useIncidents'
import { useIncidentEvents } from '@/api/hooks/useIncidentEvents'
import { useService } from '@/api/hooks/useServices'
import { useFlareHub } from '@/hooks/useFlareHub'
import { formatIncidentDuration } from '@/lib/format'

function mapConnection(state: HubConnectionState): ConnectionStatus {
  if (state === HubConnectionState.Connected) return 'connected'
  if (state === HubConnectionState.Connecting || state === HubConnectionState.Reconnecting) {
    return 'connecting'
  }
  return 'disconnected'
}

const VALID_GUID_RE = /^[0-9a-fA-F-]{36}$/

export function IncidentDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const guidLike = VALID_GUID_RE.test(id)

  const { connectionState } = useFlareHub('incident', id)
  const incidentQuery = useIncident(guidLike ? id : null)
  const incident = incidentQuery.data
  const eventsQuery = useIncidentEvents(guidLike ? id : null)
  const serviceQuery = useService(incident?.serviceId ?? null)

  const connectionStatus = mapConnection(connectionState)

  if (!guidLike || (incidentQuery.isError && !incident)) {
    return (
      <AppShell connectionStatus={connectionStatus}>
        <NotFoundCard />
      </AppShell>
    )
  }

  if (!incident) {
    return (
      <AppShell connectionStatus={connectionStatus}>
        <LoadingSkeleton />
      </AppShell>
    )
  }

  const duration = formatIncidentDuration(incident.createdAt, incident.resolvedAt)

  return (
    <AppShell connectionStatus={connectionStatus}>
      <div className="space-y-6">
        <Link
          to="/dashboard"
          className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-3 w-3" aria-hidden />
          Dashboard
        </Link>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <div className="flex items-center gap-3">
              <SeverityBadge severity={incident.severity} />
              <p
                className="font-mono text-[11px] tabular-nums text-muted-foreground"
                title="Duration since the incident opened"
              >
                {duration}
              </p>
            </div>
            <h1 className="max-w-2xl text-lg font-semibold tracking-tight text-foreground break-words">
              {incident.title}
            </h1>
            <p className="font-mono text-[11px] text-muted-foreground">
              {serviceQuery.data?.name ?? '…'} · opened {new Date(incident.createdAt).toISOString().slice(0, 16).replace('T', ' ')}Z
            </p>
          </div>
          <StatusTransitionControl
            incidentId={incident.id}
            currentStatus={incident.status}
          />
        </div>

        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
          <div className="space-y-6 min-w-0">
            <RolesPanel incident={incident} />

            <section className="space-y-3">
              <h2 className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
                Timeline
              </h2>
              <IncidentTimeline
                events={eventsQuery.data}
                loading={eventsQuery.isFetching && eventsQuery.data.length === 0}
              />
            </section>

            <CommentComposer incidentId={incident.id} />
          </div>

          <RunbookSidebar
            serviceName={serviceQuery.data?.name}
            runbookBody={serviceQuery.data?.runbookBody}
            loading={serviceQuery.isFetching && serviceQuery.data === undefined}
          />
        </div>
      </div>
    </AppShell>
  )
}

function LoadingSkeleton() {
  return (
    <div className="space-y-6">
      <div className="h-3 w-24 animate-pulse rounded-sm bg-muted" />
      <div className="space-y-2">
        <div className="h-4 w-40 animate-pulse rounded-sm bg-muted" />
        <div className="h-5 w-80 animate-pulse rounded-sm bg-muted" />
        <div className="h-3 w-56 animate-pulse rounded-sm bg-muted" />
      </div>
      <Card>
        <CardContent className="space-y-3 p-4">
          <div className="h-3 w-20 animate-pulse rounded-sm bg-muted" />
          <div className="h-7 w-full animate-pulse rounded-sm bg-muted" />
          <div className="h-7 w-full animate-pulse rounded-sm bg-muted" />
        </CardContent>
      </Card>
    </div>
  )
}

function NotFoundCard() {
  return (
    <div className="space-y-6">
      <Link
        to="/dashboard"
        className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3 w-3" aria-hidden />
        Dashboard
      </Link>
      <Card>
        <CardContent className="space-y-3 p-8 text-center">
          <p className="text-sm font-medium text-foreground">Incident not found</p>
          <p className="font-mono text-[11px] text-muted-foreground">
            The id in the URL did not match an open or archived incident.
          </p>
          <div className="pt-2">
            <Button asChild size="sm" variant="secondary">
              <Link to="/dashboard" className="font-mono text-[10px] uppercase tracking-wide">
                Back to dashboard
              </Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
