import { cva } from 'class-variance-authority'
import { cn } from '@/lib/utils'
import type { IncidentSeverity } from '@/api/types'

const dotClass = cva('h-1.5 w-1.5 rounded-full shrink-0', {
  variants: {
    severity: {
      Sev1: 'bg-red-500',
      Sev2: 'bg-amber-500',
      Sev3: 'bg-blue-500',
      Sev4: 'bg-muted-foreground/40',
    },
  },
})

const labelClass = cva('font-mono text-[11px] tracking-wide uppercase', {
  variants: {
    severity: {
      Sev1: 'text-red-500 dark:text-red-400',
      Sev2: 'text-amber-600 dark:text-amber-400',
      Sev3: 'text-blue-600 dark:text-blue-400',
      Sev4: 'text-muted-foreground',
    },
  },
})

interface SeverityBadgeProps {
  severity: IncidentSeverity
  className?: string
}

export function SeverityBadge({ severity, className }: SeverityBadgeProps) {
  return (
    <span className={cn('inline-flex items-center gap-1.5', className)}>
      <span className="relative inline-flex h-1.5 w-1.5 shrink-0">
        <span className={dotClass({ severity })} />
        {severity === 'Sev1' && (
          <span
            className={cn(dotClass({ severity }), 'absolute inset-0 animate-ping opacity-60')}
          />
        )}
      </span>
      <span className={labelClass({ severity })}>{severity.toUpperCase()}</span>
    </span>
  )
}
