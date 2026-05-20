import { useMemo } from 'react'
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from 'recharts'
import { Card } from '@/components/ui/card'
import { useMttrTrend30d } from '@/api/hooks/useMetrics'
import { computeMttrTrendDelta } from '@/lib/mttrTrend'

function formatMs(ms: number | null | undefined): string {
  if (ms == null) return '—'
  if (ms < 60_000) return `${Math.round(ms / 1000)}s`
  if (ms < 3_600_000) return `${Math.round(ms / 60_000)}m`
  return `${(ms / 3_600_000).toFixed(1)}h`
}

export function MttrTrendChart() {
  const { data, isLoading } = useMttrTrend30d()

  const points = data ?? []
  const valuesWithData = points.filter(p => p.avgMttrMs != null)
  const overallAvg = valuesWithData.length
    ? valuesWithData.reduce((sum, p) => sum + (p.avgMttrMs ?? 0), 0) / valuesWithData.length
    : null
  const delta = useMemo(() => computeMttrTrendDelta(points), [points])

  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 pt-4 pb-3">
        <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
          MTTR · 30 day trend
        </p>
        <div className="mt-2 flex items-baseline gap-2">
          <span className="font-mono text-2xl font-medium tabular-nums text-foreground">
            {formatMs(overallAvg)}
          </span>
          {delta !== null && (
            <span
              className={
                delta < 0
                  ? 'font-mono text-[11px] text-emerald-500'
                  : 'font-mono text-[11px] text-red-500'
              }
            >
              {delta < 0 ? '↓' : '↑'}
              {Math.abs(delta).toFixed(0)}% vs prior 23d
            </span>
          )}
        </div>
      </div>
      <div className="h-44 px-2 pt-2 pb-2">
        {isLoading || valuesWithData.length === 0 ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-xs text-muted-foreground">
              {isLoading ? 'Loading…' : 'No resolved incidents in window'}
            </p>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={points} margin={{ top: 8, right: 12, left: 0, bottom: 8 }}>
              <CartesianGrid strokeDasharray="2 4" stroke="var(--border)" vertical={false} />
              <XAxis
                dataKey="day"
                tickFormatter={d => (d as string).slice(5)}
                stroke="var(--muted-foreground)"
                fontSize={10}
                fontFamily="ui-monospace, monospace"
                tickLine={false}
                interval={4}
              />
              <YAxis
                tickFormatter={v => formatMs(v as number)}
                stroke="var(--muted-foreground)"
                fontSize={10}
                fontFamily="ui-monospace, monospace"
                tickLine={false}
                axisLine={false}
                width={48}
              />
              <Tooltip
                contentStyle={{
                  background: 'var(--popover)',
                  border: '1px solid var(--border)',
                  borderRadius: 6,
                  fontFamily: 'ui-monospace, monospace',
                  fontSize: 11,
                }}
                formatter={value => [formatMs(typeof value === 'number' ? value : null), 'MTTR']}
              />
              <Line
                type="monotone"
                dataKey="avgMttrMs"
                stroke="var(--chart-4)"
                strokeWidth={1.5}
                dot={false}
                connectNulls
                activeDot={{ r: 3, stroke: 'var(--chart-4)', strokeWidth: 1 }}
              />
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>
    </Card>
  )
}
