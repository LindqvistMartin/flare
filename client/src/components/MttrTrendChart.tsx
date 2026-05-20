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
import { useDashboardSummary, useMttrTrend30d } from '@/api/hooks/useMetrics'
import { computeMttrTrendDelta } from '@/lib/mttrTrend'
import { formatMs } from '@/lib/format'

export function MttrTrendChart() {
  const { data: points, isLoading, isError } = useMttrTrend30d()
  const { data: summary } = useDashboardSummary()

  const series = points ?? []
  const valuesWithData = series.filter(p => p.avgMttrMs != null)
  const delta = useMemo(() => computeMttrTrendDelta(series), [series])

  // Headline number comes from the backend dashboard summary so the value above
  // the chart matches the value shown in the StatCard row. Computing it from
  // daily means here would be mean-of-means, which disagrees with the summary's
  // per-service-weighted average and confuses the reader.
  const headline = summary?.mttrLast30dAvgMs ?? null

  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 pt-4 pb-3">
        <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
          MTTR · 30 day trend
        </p>
        <div className="mt-2 flex items-baseline gap-2">
          <span className="font-mono text-2xl font-medium tabular-nums text-foreground">
            {formatMs(headline)}
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
        {isLoading ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-xs text-muted-foreground">Loading…</p>
          </div>
        ) : isError ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-xs text-muted-foreground">Trend unavailable</p>
          </div>
        ) : valuesWithData.length === 0 ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-xs text-muted-foreground">
              No resolved incidents in window
            </p>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={series} margin={{ top: 8, right: 12, left: 0, bottom: 8 }}>
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
