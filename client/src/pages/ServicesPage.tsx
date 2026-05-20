import { AppShell } from '@/components/AppShell'
import { Card, CardContent } from '@/components/ui/card'
import { useServices } from '@/api/hooks/useServices'

export function ServicesPage() {
  const { data: services } = useServices()
  return (
    <AppShell>
      <div className="space-y-6">
        <div className="flex items-baseline justify-between">
          <h1 className="text-lg font-semibold tracking-tight text-foreground">Services</h1>
          <span className="font-mono text-[11px] text-muted-foreground">
            {services.length} registered
          </span>
        </div>
        <Card>
          <CardContent className="p-8 text-center">
            <p className="text-sm font-medium text-foreground">Service catalogue in development</p>
            <p className="mt-1 font-mono text-[11px] text-muted-foreground">
              Runbook editor and per-service incident history land in a follow-up.
            </p>
          </CardContent>
        </Card>
      </div>
    </AppShell>
  )
}
