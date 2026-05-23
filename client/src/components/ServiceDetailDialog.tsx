import { useMemo, useState } from 'react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { RunbookEditor } from '@/components/RunbookEditor'
import { SeverityBadge } from '@/components/SeverityBadge'
import { StatusChip } from '@/components/StatusChip'
import { useIncidents } from '@/api/hooks/useIncidents'
import { useUpdateServiceRunbook } from '@/api/hooks/useServiceMutations'
import { useDebouncedCallback } from '@/hooks/useDebouncedCallback'
import { formatTimestampUtc } from '@/lib/format'
import type { Service } from '@/api/types'

interface ServiceDetailDialogProps {
  service: Service | null
  onOpenChange: (open: boolean) => void
}

// Outer dialog manages open/close; the body is its own component, keyed by
// service.id so opening a different service remounts with fresh local state
// (the runbook draft). That sidesteps the useEffect-resets-state anti-pattern
// caught by react-hooks/set-state-in-effect.
export function ServiceDetailDialog({ service, onOpenChange }: ServiceDetailDialogProps) {
  const open = service !== null
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl">
        {service && <ServiceDetailBody key={service.id} service={service} />}
      </DialogContent>
    </Dialog>
  )
}

interface ServiceDetailBodyProps {
  service: Service
}

const HISTORY_LIMIT = 10

function ServiceDetailBody({ service }: ServiceDetailBodyProps) {
  const incidents = useIncidents({ serviceId: service.id })
  const mutation = useUpdateServiceRunbook(service.id)
  const [draft, setDraft] = useState<string>(service.runbookBody)
  const dirty = draft !== service.runbookBody

  const fireSave = useDebouncedCallback((body: string) => {
    if (body === service.runbookBody) return
    mutation.mutate({ runbookBody: body })
  }, 500)

  const handleChange = (next: string) => {
    setDraft(next)
    fireSave(next)
  }

  const history = useMemo(() => {
    return [...incidents.data]
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      .slice(0, HISTORY_LIMIT)
  }, [incidents.data])

  return (
    <>
      <DialogHeader>
        <DialogTitle className="flex items-center gap-2 text-base font-semibold">
          <span>{service.name}</span>
          <span className="font-mono text-[11px] font-normal text-muted-foreground">
            · {history.length} incident{history.length === 1 ? '' : 's'}
          </span>
        </DialogTitle>
        {service.description && (
          <DialogDescription>{service.description}</DialogDescription>
        )}
      </DialogHeader>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
        <section className="min-w-0 space-y-3">
          <h3 className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
            Runbook
          </h3>
          <RunbookEditor
            value={draft}
            onChange={handleChange}
            saving={mutation.isPending}
            dirty={dirty}
          />
        </section>

        <aside className="space-y-3">
          <h3 className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
            Recent incidents
          </h3>
          {!incidents.isFetched ? (
            <div className="space-y-1.5">
              {Array.from({ length: 3 }).map((_, i) => (
                <div key={i} className="h-10 w-full animate-pulse rounded-sm bg-muted" />
              ))}
            </div>
          ) : history.length === 0 ? (
            <p className="font-mono text-[11px] text-muted-foreground">
              No incidents recorded for this service yet.
            </p>
          ) : (
            <ul className="space-y-2">
              {history.map(inc => (
                <li
                  key={inc.id}
                  className="space-y-1 rounded-sm border border-border bg-card/40 p-2"
                >
                  <div className="flex items-center gap-2">
                    <SeverityBadge severity={inc.severity} />
                    <StatusChip status={inc.status} />
                  </div>
                  <p className="text-sm text-foreground break-words" title={inc.title}>
                    {inc.title}
                  </p>
                  <p className="font-mono text-[10px] tabular-nums text-muted-foreground">
                    {formatTimestampUtc(inc.createdAt)}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>
    </>
  )
}
