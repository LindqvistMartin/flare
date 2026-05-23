import { useMemo } from 'react'
import { cn } from '@/lib/utils'
import {
  ACTION_ITEM_FILTERS,
  countByFilter,
  type ActionItemFilterKey,
} from '@/lib/actionItemFilter'
import type { ActionItem } from '@/api/types'

const FILTER_LABEL: Record<ActionItemFilterKey, string> = {
  All: 'All',
  Open: 'Open',
  InProgress: 'In progress',
  Done: 'Done',
  Overdue: 'Overdue',
}

interface ActionItemFilterTabsProps {
  items: ActionItem[]
  active: ActionItemFilterKey
  onChange: (next: ActionItemFilterKey) => void
  now?: Date
}

// Pill row of mutually-exclusive filters with live count. The Overdue tile
// flips to a warning tone when its count is non-zero — a single attention pull
// that matches the dashboard severity ribbon convention.
export function ActionItemFilterTabs({ items, active, onChange, now }: ActionItemFilterTabsProps) {
  const counts = useMemo(() => countByFilter(items, now), [items, now])

  return (
    <div
      role="tablist"
      aria-label="Action item filter"
      className="flex flex-wrap items-center gap-1.5"
    >
      {ACTION_ITEM_FILTERS.map(key => {
        const isActive = active === key
        const isOverdue = key === 'Overdue' && counts[key] > 0
        return (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={isActive}
            onClick={() => onChange(key)}
            className={cn(
              'group inline-flex items-center gap-2 rounded-sm border px-2.5 py-1 transition-colors',
              'focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring',
              isActive
                ? 'border-foreground/40 bg-foreground/5 text-foreground'
                : 'border-border bg-transparent text-muted-foreground hover:bg-muted/40 hover:text-foreground',
              !isActive && isOverdue && 'text-red-600 dark:text-red-300 border-red-500/30',
            )}
          >
            <span className="font-mono text-[11px] uppercase tracking-wide">
              {FILTER_LABEL[key]}
            </span>
            <span
              className={cn(
                'rounded-sm px-1 font-mono text-[10px] tabular-nums',
                isActive ? 'bg-foreground/10 text-foreground' : 'bg-muted/60 text-muted-foreground',
                !isActive && isOverdue && 'bg-red-500/10 text-red-600 dark:text-red-300',
              )}
            >
              {counts[key]}
            </span>
          </button>
        )
      })}
    </div>
  )
}
