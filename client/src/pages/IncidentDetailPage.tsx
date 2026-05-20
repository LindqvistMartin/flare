import { useParams } from 'react-router-dom'
import { AppShell } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'

export function IncidentDetailPage() {
  const { id } = useParams<{ id: string }>()
  return (
    <AppShell>
      <div className="space-y-6">
        <div>
          <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
            Incident
          </p>
          <h1 className="mt-1 font-mono text-xl tabular-nums text-foreground">{id}</h1>
        </div>
        <Card>
          <CardContent className="p-8 text-center">
            <p className="text-sm font-medium text-foreground">Detail view in development</p>
            <p className="mt-1 font-mono text-[11px] text-muted-foreground">
              Timeline feed, role assignment, and status controls land in a follow-up.
            </p>
          </CardContent>
        </Card>
      </div>
    </AppShell>
  )
}
