import { AppShell } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'
import { useActionItems } from '@/api/hooks/useActionItems'

export function ActionItemsPage() {
  const { data: items } = useActionItems()
  const { data: overdue } = useActionItems({ overdue: true })
  return (
    <AppShell>
      <div className="space-y-6">
        <div className="flex items-baseline justify-between">
          <h1 className="text-lg font-semibold tracking-tight text-foreground">Action items</h1>
          <span className="font-mono text-[11px] text-muted-foreground">
            {items.length} total · {overdue.length} overdue
          </span>
        </div>
        <Card>
          <CardContent className="p-8 text-center">
            <p className="text-sm font-medium text-foreground">Tracker in development</p>
            <p className="mt-1 font-mono text-[11px] text-muted-foreground">
              Inline status toggles, owner assignment, and filtering land in a follow-up.
            </p>
          </CardContent>
        </Card>
      </div>
    </AppShell>
  )
}
