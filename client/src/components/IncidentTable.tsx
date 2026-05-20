import { useMemo } from 'react'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { formatDistanceToNowStrict } from 'date-fns'
import { SeverityBadge } from '@/components/SeverityBadge'
import { StatusChip } from '@/components/StatusChip'
import type { Incident, IncidentSeverity, Service } from '@/api/types'
import { cn } from '@/lib/utils'

const severityRibbonClass: Record<IncidentSeverity, string> = {
  Sev1: 'before:bg-red-500',
  Sev2: 'before:bg-amber-500',
  Sev3: 'before:bg-blue-500',
  Sev4: 'before:bg-muted-foreground/30',
}

function formatDuration(opened: string, resolved: string | null): string {
  const start = new Date(opened).getTime()
  const end = resolved ? new Date(resolved).getTime() : Date.now()
  const ms = Math.max(0, end - start)
  if (ms < 60_000) return `${Math.round(ms / 1000)}s`
  if (ms < 3_600_000) return `${Math.round(ms / 60_000)}m`
  if (ms < 86_400_000) return `${(ms / 3_600_000).toFixed(1)}h`
  return `${(ms / 86_400_000).toFixed(1)}d`
}

interface IncidentTableProps {
  incidents: Incident[]
  services: Service[]
  loading?: boolean
}

export function IncidentTable({ incidents, services, loading }: IncidentTableProps) {
  const serviceMap = useMemo(() => new Map(services.map(s => [s.id, s])), [services])

  if (loading && incidents.length === 0) {
    return (
      <div className="rounded-md border border-border bg-card/40">
        <div className="px-6 py-12 text-center">
          <p className="font-mono text-xs text-muted-foreground">Loading incidents…</p>
        </div>
      </div>
    )
  }

  if (incidents.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border bg-card/20">
        <div className="px-6 py-12 text-center">
          <p className="text-sm font-medium text-foreground">No active incidents</p>
          <p className="mt-1 font-mono text-[11px] text-muted-foreground">
            All systems operational
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="overflow-hidden rounded-md border border-border bg-card/40">
      <Table>
        <TableHeader>
          <TableRow className="hover:bg-transparent">
            <TableHead className="font-mono text-[10px] uppercase tracking-wide">
              Severity
            </TableHead>
            <TableHead className="font-mono text-[10px] uppercase tracking-wide">
              Status
            </TableHead>
            <TableHead className="font-mono text-[10px] uppercase tracking-wide">
              Service
            </TableHead>
            <TableHead className="font-mono text-[10px] uppercase tracking-wide">Title</TableHead>
            <TableHead className="font-mono text-[10px] uppercase tracking-wide">Opened</TableHead>
            <TableHead className="text-right font-mono text-[10px] uppercase tracking-wide">
              Duration
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {incidents.map(incident => {
            const svc = serviceMap.get(incident.serviceId)
            return (
              <TableRow
                key={incident.id}
                className={cn(
                  'relative before:absolute before:left-0 before:top-0 before:h-full before:w-0.5 before:content-[""]',
                  severityRibbonClass[incident.severity],
                )}
              >
                <TableCell className="pl-5">
                  <SeverityBadge severity={incident.severity} />
                </TableCell>
                <TableCell>
                  <StatusChip status={incident.status} />
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {svc?.name ?? '—'}
                </TableCell>
                <TableCell className="max-w-md truncate text-sm text-foreground">
                  {incident.title}
                </TableCell>
                <TableCell className="font-mono text-[11px] text-muted-foreground">
                  {formatDistanceToNowStrict(new Date(incident.createdAt), { addSuffix: true })}
                </TableCell>
                <TableCell className="text-right font-mono text-[11px] tabular-nums text-muted-foreground">
                  {formatDuration(incident.createdAt, incident.resolvedAt)}
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
