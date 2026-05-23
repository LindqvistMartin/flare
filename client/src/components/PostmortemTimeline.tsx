import { formatDistanceToNowStrict } from 'date-fns'
import { iconForEventType, shortActor } from '@/lib/timeline'
import type { PostmortemTimelineEntry } from '@/api/types'

interface PostmortemTimelineProps {
  entries: PostmortemTimelineEntry[]
}

// Read-only timeline rendered straight from the postmortem.timeline JSON
// payload. Builder summaries are already truncated server-side, so the row
// shape stays predictable. The component is a sibling of IncidentTimeline —
// same visual language, no shared base because the event shapes differ.
export function PostmortemTimeline({ entries }: PostmortemTimelineProps) {
  if (entries.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border bg-card/20 px-6 py-8 text-center">
        <p className="font-mono text-[11px] text-muted-foreground">
          No timeline events captured.
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
      {entries.map((entry, idx) => {
        const Icon = iconForEventType(entry.Type)
        return (
          <li key={`${entry.At}-${idx}`} className="relative">
            <span
              className="absolute -left-6 top-1 flex h-3.5 w-3.5 items-center justify-center rounded-full border border-border bg-background"
              aria-hidden
            >
              <Icon className="h-2 w-2 text-muted-foreground" strokeWidth={2.5} />
            </span>
            <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
              <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
                {entry.Type}
              </p>
              <p
                className="font-mono text-[10px] tabular-nums text-muted-foreground"
                title={new Date(entry.At).toISOString()}
              >
                {formatDistanceToNowStrict(new Date(entry.At), { addSuffix: true })}
              </p>
              <p className="font-mono text-[10px] text-muted-foreground">
                · {shortActor(entry.ActorId)}
              </p>
            </div>
            <p className="mt-0.5 text-sm text-foreground break-words">
              {entry.Summary}
            </p>
          </li>
        )
      })}
    </ol>
  )
}
