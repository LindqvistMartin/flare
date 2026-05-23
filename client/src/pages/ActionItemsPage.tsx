import { useMemo } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { CheckCircle2, Clock } from 'lucide-react'
import { AppShell } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ActionItemFilterTabs } from '@/components/ActionItemFilterTabs'
import { ActionItemStatusBadge } from '@/components/ActionItemStatusBadge'
import { useActionItems } from '@/api/hooks/useActionItems'
import {
  ACTION_ITEM_FILTERS,
  filterActionItems,
  isActionItemOverdue,
  type ActionItemFilterKey,
} from '@/lib/actionItemFilter'
import { cn } from '@/lib/utils'
import type { ActionItem } from '@/api/types'

const FILTER_PARAM = 'filter'

function readFilterParam(value: string | null): ActionItemFilterKey {
  return (ACTION_ITEM_FILTERS as readonly string[]).includes(value ?? '')
    ? (value as ActionItemFilterKey)
    : 'All'
}

function ownerInitials(ownerId: string | null): string {
  if (ownerId === null) return '—'
  return ownerId.slice(0, 2).toUpperCase()
}

function formatDueDate(due: string | null, overdue: boolean): React.ReactNode {
  if (due === null) {
    return <span className="text-muted-foreground">—</span>
  }
  return (
    <span
      className={cn(
        'font-mono tabular-nums',
        overdue ? 'text-red-600 dark:text-red-300' : 'text-foreground',
      )}
      title={overdue ? 'Past due date' : undefined}
    >
      {due}
    </span>
  )
}

export function ActionItemsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const filter = readFilterParam(searchParams.get(FILTER_PARAM))

  const { data: items, isFetched } = useActionItems()
  const now = useMemo(() => new Date(), [])
  const filtered = useMemo(
    () => filterActionItems(items, filter, now),
    [items, filter, now],
  )

  const setFilter = (next: ActionItemFilterKey) => {
    setSearchParams(
      prev => {
        const out = new URLSearchParams(prev)
        if (next === 'All') out.delete(FILTER_PARAM)
        else out.set(FILTER_PARAM, next)
        return out
      },
      { replace: true },
    )
  }

  return (
    <AppShell>
      <div className="space-y-6">
        <header className="flex flex-wrap items-baseline justify-between gap-3">
          <div className="space-y-1">
            <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
              Tracker
            </p>
            <h1 className="text-lg font-semibold tracking-tight text-foreground">
              Action items
            </h1>
          </div>
          <p className="font-mono text-[11px] tabular-nums text-muted-foreground">
            {items.length} total
          </p>
        </header>

        <ActionItemFilterTabs
          items={items}
          active={filter}
          onChange={setFilter}
          now={now}
        />

        {!isFetched ? (
          <LoadingSkeleton />
        ) : filtered.length === 0 ? (
          <EmptyState filter={filter} />
        ) : (
          <Card className="overflow-hidden p-0">
            <CardContent className="p-0">
              <Table>
                <TableHeader>
                  <TableRow className="border-b border-border">
                    <TableHead className="font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
                      Title
                    </TableHead>
                    <TableHead className="font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
                      Owner
                    </TableHead>
                    <TableHead className="font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
                      Due
                    </TableHead>
                    <TableHead className="text-right font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
                      Status
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filtered.map(item => (
                    <ActionItemRow key={item.id} item={item} now={now} />
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </div>
    </AppShell>
  )
}

interface ActionItemRowProps {
  item: ActionItem
  now: Date
}

function ActionItemRow({ item, now }: ActionItemRowProps) {
  const overdue = isActionItemOverdue(item, now)
  return (
    <TableRow className="group">
      <TableCell className="max-w-[420px]">
        <div className="flex items-start gap-2">
          {item.status === 'Done' ? (
            <CheckCircle2
              className="mt-0.5 h-3 w-3 shrink-0 text-emerald-500"
              aria-hidden
            />
          ) : overdue ? (
            <Clock
              className="mt-0.5 h-3 w-3 shrink-0 text-red-500"
              aria-hidden
            />
          ) : null}
          <Link
            to="/dashboard"
            className="block truncate text-sm text-foreground underline-offset-4 hover:underline"
            title={item.title}
          >
            {item.title}
          </Link>
        </div>
      </TableCell>
      <TableCell>
        <span
          className="inline-flex h-6 w-6 items-center justify-center rounded-full border border-border bg-muted/40 font-mono text-[10px] uppercase tabular-nums text-muted-foreground"
          title={item.ownerId ?? 'unassigned'}
          aria-label={item.ownerId ? `Owner ${item.ownerId}` : 'No owner'}
        >
          {ownerInitials(item.ownerId)}
        </span>
      </TableCell>
      <TableCell>{formatDueDate(item.dueDate, overdue)}</TableCell>
      <TableCell className="text-right">
        <ActionItemStatusBadge itemId={item.id} status={item.status} />
      </TableCell>
    </TableRow>
  )
}

function LoadingSkeleton() {
  return (
    <Card>
      <CardContent className="space-y-2 p-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-6 w-full animate-pulse rounded-sm bg-muted" />
        ))}
      </CardContent>
    </Card>
  )
}

interface EmptyStateProps {
  filter: ActionItemFilterKey
}

const EMPTY_COPY: Record<ActionItemFilterKey, { title: string; detail: string }> = {
  All: {
    title: 'No action items yet',
    detail: 'They appear here as soon as a postmortem records one.',
  },
  Open: {
    title: 'No open items',
    detail: 'Anything not yet started will show up here.',
  },
  InProgress: {
    title: 'Nothing in progress',
    detail: 'Items the team has picked up but not finished.',
  },
  Done: {
    title: 'Nothing closed out yet',
    detail: 'Items become Done once their owner ships the fix.',
  },
  Overdue: {
    title: 'No overdue action items — nice work',
    detail: 'Items past their due date but not yet Done would land here.',
  },
}

function EmptyState({ filter }: EmptyStateProps) {
  const copy = EMPTY_COPY[filter]
  return (
    <Card>
      <CardContent className="space-y-2 p-8 text-center">
        <p className="text-sm font-medium text-foreground">{copy.title}</p>
        <p className="mx-auto max-w-md font-mono text-[11px] text-muted-foreground">
          {copy.detail}
        </p>
      </CardContent>
    </Card>
  )
}
