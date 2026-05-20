import { cva } from 'class-variance-authority'
import { cn } from '@/lib/utils'
import type { IncidentStatus } from '@/api/types'

const chipClass = cva(
  'inline-flex items-center gap-1.5 rounded-sm border px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide',
  {
    variants: {
      status: {
        Triggered:     'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-300',
        Investigating: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-300',
        Identified:    'border-blue-500/30 bg-blue-500/10 text-blue-600 dark:text-blue-300',
        Monitoring:    'border-violet-500/30 bg-violet-500/10 text-violet-600 dark:text-violet-300',
        Resolved:      'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-300',
        Closed:        'border-border bg-muted text-muted-foreground',
      },
    },
  },
)

interface StatusChipProps {
  status: IncidentStatus
  className?: string
}

export function StatusChip({ status, className }: StatusChipProps) {
  return <span className={cn(chipClass({ status }), className)}>{status}</span>
}
