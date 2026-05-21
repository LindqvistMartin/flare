import { formatDistanceToNowStrict } from 'date-fns'
import { iconForEventType, summarizeEvent, shortActor } from '@/lib/timeline'
import type { IncidentEvent } from '@/api/types'

interface IncidentTimelineProps {
  events: IncidentEvent[]
  loading?: boolean
}

export function IncidentTimeline({ events, loading }: IncidentTimelineProps) {
  if (loading && events.length === 0) {
    return (
      <div className="rounded-md border border-border bg-card/40 px-6 py-10 text-center">
        <p className="font-mono text-[11px] text-muted-foreground">Loading timeline…</p>
      </div>
    )
  }

  if (events.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border bg-card/20 px-6 py-10 text-center">
        <p className="text-sm font-medium text-foreground">No events yet</p>
        <p className="mt-1 font-mono text-[11px] text-muted-foreground">
          Status transitions, role assignments, and comments appear here.
        </p>
      </div>
    )
  }

  return (
    <ol className="relative space-y-3 pl-6">
      <span
        className="pointer-events-none absolute left-[7px] top-2 bottom-2 w-px bg-border"
        aria-hidden
      />
      {events.map(event => {
        const Icon = iconForEventType(event.type)
        return (
          <li key={event.id} className="relative">
            <span
              className="absolute -left-6 top-1 flex h-3.5 w-3.5 items-center justify-center rounded-full border border-border bg-background"
              aria-hidden
            >
              <Icon className="h-2 w-2 text-muted-foreground" strokeWidth={2.5} />
            </span>
            <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
              <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
                {event.type}
              </p>
              <p
                className="font-mono text-[10px] tabular-nums text-muted-foreground"
                title={new Date(event.createdAt).toISOString()}
              >
                {formatDistanceToNowStrict(new Date(event.createdAt), { addSuffix: true })}
              </p>
              <p className="font-mono text-[10px] text-muted-foreground">
                · {shortActor(event.actorId)}
              </p>
            </div>
            <p className="mt-0.5 text-sm text-foreground break-words">
              {summarizeEvent(event)}
            </p>
          </li>
        )
      })}
    </ol>
  )
}
