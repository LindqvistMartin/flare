import { type ReactNode } from 'react'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'

type StatCardTone = 'default' | 'warning' | 'alert' | 'positive'

interface StatCardProps {
  eyebrow: string
  value: ReactNode
  sublabel?: string
  tone?: StatCardTone
  className?: string
}

const toneStripClass: Record<StatCardTone, string> = {
  default:  'bg-border',
  warning:  'bg-amber-500',
  alert:    'bg-red-500',
  positive: 'bg-emerald-500',
}

export function StatCard({ eyebrow, value, sublabel, tone = 'default', className }: StatCardProps) {
  return (
    <Card className={cn('relative overflow-hidden', className)}>
      <span
        className={cn('absolute left-0 top-0 h-full w-0.5', toneStripClass[tone])}
        aria-hidden
      />
      <CardContent className="p-4 pl-5">
        <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
          {eyebrow}
        </p>
        <div className="mt-2 flex items-baseline gap-2">
          <span className="font-mono text-3xl font-medium tabular-nums text-foreground">
            {value}
          </span>
          {sublabel && (
            <span className="font-mono text-[11px] text-muted-foreground">{sublabel}</span>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
