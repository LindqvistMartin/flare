import { useCallback } from 'react'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { cn } from '@/lib/utils'
import { useUpdateActionItemStatus } from '@/api/hooks/useActionItemMutations'
import type { ActionItemStatus } from '@/api/types'

const tone: Record<ActionItemStatus, string> = {
  Open:       'border-blue-500/30 bg-blue-500/10 text-blue-600 dark:text-blue-300',
  InProgress: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-300',
  Done:       'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-300',
}

const label: Record<ActionItemStatus, string> = {
  Open: 'OPEN',
  InProgress: 'IN PROGRESS',
  Done: 'DONE',
}

interface ActionItemStatusBadgeProps {
  itemId: string
  status: ActionItemStatus
  disabled?: boolean
}

// Compact inline status toggle. Looks like a chip, behaves like a Radix Select.
// Remounts on status change (key={status}) so the trigger always shows the
// current value as plain text rather than relying on controlled-value reconciliation,
// which has bitten the incident detail page when Radix dedupes same-value picks.
export function ActionItemStatusBadge({ itemId, status, disabled }: ActionItemStatusBadgeProps) {
  const mutation = useUpdateActionItemStatus(itemId)
  const onValueChange = useCallback(
    (value: string) => {
      mutation.mutate({ status: value as ActionItemStatus })
    },
    [mutation],
  )

  return (
    <Select
      key={`status-${status}`}
      onValueChange={onValueChange}
      disabled={disabled || mutation.isPending}
    >
      <SelectTrigger
        aria-label="Change action item status"
        className={cn(
          'h-6 w-auto gap-1 rounded-sm border px-1.5 py-0 font-mono text-[10px] uppercase tracking-wide focus:ring-offset-0',
          tone[status],
        )}
      >
        <SelectValue placeholder={label[status]} />
      </SelectTrigger>
      <SelectContent align="end">
        <SelectItem value="Open" className="font-mono text-[11px]">OPEN</SelectItem>
        <SelectItem value="InProgress" className="font-mono text-[11px]">IN PROGRESS</SelectItem>
        <SelectItem value="Done" className="font-mono text-[11px]">DONE</SelectItem>
      </SelectContent>
    </Select>
  )
}
