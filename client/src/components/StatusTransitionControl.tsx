import { useCallback } from 'react'
import { ArrowRight } from 'lucide-react'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { StatusChip } from '@/components/StatusChip'
import { allowedNextStatuses, isTerminalStatus } from '@/lib/incidentTransitions'
import { useTransitionIncident } from '@/api/hooks/useIncidentMutations'
import type { IncidentStatus } from '@/api/types'

interface StatusTransitionControlProps {
  incidentId: string
  currentStatus: IncidentStatus
}

export function StatusTransitionControl({ incidentId, currentStatus }: StatusTransitionControlProps) {
  const transition = useTransitionIncident(incidentId)
  const next = allowedNextStatuses(currentStatus)
  const terminal = isTerminalStatus(currentStatus)

  const onValueChange = useCallback(
    (value: string) => transition.mutate({ to: value as IncidentStatus }),
    [transition],
  )

  return (
    <div className="flex items-center gap-2">
      <StatusChip status={currentStatus} />
      <ArrowRight className="h-3 w-3 text-muted-foreground" aria-hidden />
      {/* The Select is uncontrolled: a fresh key remounts it whenever the
          incident transitions (via onSuccess or SignalR echo), so the trigger
          shows the placeholder again instead of holding the user's last pick.
          Controlled `value=""` would have Radix dedupe same-value picks; the
          remount sidesteps that fragility entirely. */}
      <Select
        key={`transition-${currentStatus}`}
        disabled={terminal || transition.isPending}
        onValueChange={onValueChange}
      >
        <SelectTrigger
          className="h-7 w-40 font-mono text-[11px]"
          aria-label="Transition incident status"
        >
          <SelectValue placeholder={terminal ? 'Terminal' : 'Transition to…'} />
        </SelectTrigger>
        <SelectContent>
          {next.map(status => (
            <SelectItem key={status} value={status} className="font-mono text-[11px]">
              {status}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
