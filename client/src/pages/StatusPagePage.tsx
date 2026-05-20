import { useParams } from 'react-router-dom'

export function StatusPagePage() {
  const { slug } = useParams<{ slug: string }>()
  return (
    <div className="min-h-screen bg-background">
      <div className="mx-auto max-w-2xl px-6 py-24">
        <div className="flex items-center gap-2">
          <div className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
          <p className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground">
            STATUS · /{slug}
          </p>
        </div>
        <h1 className="mt-4 text-3xl font-semibold tracking-tight text-foreground">
          Public status page in development
        </h1>
        <p className="mt-3 max-w-md text-sm text-muted-foreground">
          The customer-facing layout — current per-service status, 30-day history bars, and
          active incident summaries — lands in a follow-up. The backend endpoint at{' '}
          <code className="font-mono text-xs text-foreground">GET /public/status/{slug}</code>{' '}
          already serves the data.
        </p>
        <p className="mt-12 font-mono text-[10px] tracking-wide uppercase text-muted-foreground/60">
          Powered by Flare
        </p>
      </div>
    </div>
  )
}
