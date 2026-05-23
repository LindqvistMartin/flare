import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { SeverityBadge } from '@/components/SeverityBadge'
import { formatDistanceToNowStrict } from 'date-fns'
import { FileText } from 'lucide-react'
import type { Incident, Service } from '@/api/types'
import { cn } from '@/lib/utils'

interface ServiceCardProps {
  service: Service
  incidents: Incident[]
  windowDays: number
  onOpen: (service: Service) => void
}

export function ServiceCard({ service, incidents, windowDays, onOpen }: ServiceCardProps) {
  const last = incidents[0] ?? null
  const hasRunbook = service.runbookBody.trim().length > 0

  return (
    <Card
      className={cn(
        'group relative overflow-hidden p-0 transition-colors hover:border-foreground/30',
        // Left-edge tone strip mirrors StatCard / IncidentTable convention: a
        // service with a Sev1 still active reads as alert at a glance.
        last?.status !== 'Closed' && last?.severity === 'Sev1' &&
          'before:absolute before:left-0 before:top-0 before:h-full before:w-0.5 before:bg-red-500 before:content-[""]',
        last?.status !== 'Closed' && last?.severity === 'Sev2' &&
          'before:absolute before:left-0 before:top-0 before:h-full before:w-0.5 before:bg-amber-500 before:content-[""]',
      )}
    >
      <CardContent className="space-y-3 p-4">
        <div className="flex items-baseline justify-between gap-3">
          <h3 className="text-sm font-semibold tracking-tight text-foreground break-words">
            {service.name}
          </h3>
          <span className="font-mono text-[10px] tabular-nums uppercase tracking-wide text-muted-foreground">
            {incidents.length} / {windowDays}d
          </span>
        </div>

        {service.description ? (
          <p className="line-clamp-2 text-[12px] text-muted-foreground">
            {service.description}
          </p>
        ) : (
          <p className="font-mono text-[11px] text-muted-foreground/70">
            No description.
          </p>
        )}

        <div className="space-y-1.5">
          <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
            Last incident
          </p>
          {last ? (
            <div className="flex items-center gap-2">
              <SeverityBadge severity={last.severity} />
              <span
                className="font-mono text-[10px] tabular-nums text-muted-foreground"
                title={new Date(last.createdAt).toISOString()}
              >
                {formatDistanceToNowStrict(new Date(last.createdAt), { addSuffix: true })}
              </span>
            </div>
          ) : (
            <p className="font-mono text-[11px] text-muted-foreground">—</p>
          )}
        </div>

        <div className="flex items-center justify-between pt-1">
          <span
            className={cn(
              'font-mono text-[10px] uppercase tracking-wide',
              hasRunbook ? 'text-emerald-600 dark:text-emerald-400' : 'text-muted-foreground',
            )}
          >
            {hasRunbook ? 'Runbook ready' : 'No runbook'}
          </span>
          <Button
            size="sm"
            variant="secondary"
            onClick={() => onOpen(service)}
            className="font-mono text-[10px] uppercase tracking-wide"
          >
            <FileText className="mr-1.5 h-3 w-3" aria-hidden />
            {hasRunbook ? 'Edit' : 'Add runbook'}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
