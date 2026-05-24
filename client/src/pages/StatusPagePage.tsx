import { useParams } from 'react-router-dom'
import { isAxiosError } from 'axios'
import { formatDistanceToNowStrict } from 'date-fns'
import { usePublicStatus } from '@/api/hooks/usePublicStatus'
import type { PublicServiceStatus, PublicStatusPage } from '@/api/publicTypes'
import {
  countActiveIncidents,
  overallStatusLabel,
  overallStatusTone,
  serviceStatusLabel,
  serviceStatusTone,
  type StatusTone,
} from '@/lib/publicStatus'
import { SeverityBadge } from '@/components/SeverityBadge'
import { cn } from '@/lib/utils'

const TONE_DOT: Record<StatusTone, string> = {
  positive: 'bg-emerald-500',
  warning: 'bg-amber-500',
  alert: 'bg-red-500',
  muted: 'bg-muted-foreground/40',
}

const TONE_TEXT: Record<StatusTone, string> = {
  positive: 'text-emerald-600 dark:text-emerald-400',
  warning: 'text-amber-600 dark:text-amber-400',
  alert: 'text-red-600 dark:text-red-400',
  muted: 'text-muted-foreground',
}

export function StatusPagePage() {
  const { slug } = useParams<{ slug: string }>()
  const query = usePublicStatus(slug)

  // React Router guarantees the param for /p/:slug, but a manual /#/p/ visit
  // lands here with an undefined slug — hook stays disabled (isPending=true,
  // no data, no error) and the page would render an infinite skeleton without
  // this guard.
  if (!slug) {
    return (
      <div className="min-h-screen bg-background">
        <div className="mx-auto max-w-2xl px-6 py-16 sm:py-24">
          <NotFoundShell slug="" />
          <Footer />
        </div>
      </div>
    )
  }

  const notFound = isAxiosError(query.error) && query.error.response?.status === 404
  // A successful previous load that is now refetching with an error keeps
  // query.data populated — keep showing the last good snapshot rather than
  // flashing an error shell over a working page.
  const networkError = query.error && !notFound && !query.data

  return (
    <div className="min-h-screen bg-background">
      <div className="mx-auto max-w-2xl px-6 py-16 sm:py-24">
        {query.data && <StatusShell page={query.data} />}
        {!query.data && notFound && <NotFoundShell slug={slug} />}
        {!query.data && networkError && <NetworkErrorShell slug={slug} />}
        {!query.data && !query.error && query.isPending && <LoadingShell slug={slug} />}
        <Footer generatedAt={query.data?.generatedAt} />
      </div>
    </div>
  )
}

function SlugEyebrow({ slug }: { slug: string }) {
  return (
    <p className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground">
      STATUS · /{slug}
    </p>
  )
}

function LoadingShell({ slug }: { slug: string }) {
  return (
    <div>
      <SlugEyebrow slug={slug} />
      <div className="mt-4 h-9 w-72 rounded-md bg-muted/60 animate-pulse" />
      <div className="mt-3 h-5 w-48 rounded-md bg-muted/40 animate-pulse" />
      <div className="mt-12 space-y-3">
        {[0, 1, 2].map(i => (
          <div key={i} className="h-12 rounded-lg border border-border/60 bg-muted/20 animate-pulse" />
        ))}
      </div>
    </div>
  )
}

function NotFoundShell({ slug }: { slug: string }) {
  return (
    <div>
      <SlugEyebrow slug={slug} />
      <h1 className="mt-4 text-3xl font-semibold tracking-tight text-foreground">
        This status page doesn't exist
      </h1>
      <p className="mt-3 max-w-md text-sm text-muted-foreground">
        No status page is published at this address. Double-check the link or contact the
        operator if you expected one to be here.
      </p>
    </div>
  )
}

function NetworkErrorShell({ slug }: { slug: string }) {
  return (
    <div>
      <SlugEyebrow slug={slug} />
      <h1 className="mt-4 text-3xl font-semibold tracking-tight text-foreground">
        Status unavailable
      </h1>
      <p className="mt-3 max-w-md text-sm text-muted-foreground">
        We couldn't reach the status backend. Try again in a moment — this usually clears itself.
      </p>
    </div>
  )
}

function StatusShell({ page }: { page: PublicStatusPage }) {
  const tone = overallStatusTone(page.overallStatus)
  const activeCount = countActiveIncidents(page.services)

  return (
    <div>
      <div className="flex items-center gap-2">
        <span className={cn('h-1.5 w-1.5 rounded-full', TONE_DOT[tone])} />
        <SlugEyebrow slug={page.slug} />
      </div>
      <h1 className={cn('mt-4 text-3xl font-semibold tracking-tight', TONE_TEXT[tone])}>
        {overallStatusLabel(page.overallStatus)}
      </h1>
      <p className="mt-3 text-sm text-muted-foreground">
        {page.title}
        {page.description && <span className="text-muted-foreground/80"> — {page.description}</span>}
      </p>

      <section className="mt-12">
        <h2 className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground">
          Services
        </h2>
        {page.services.length === 0 ? (
          <p className="mt-3 text-sm text-muted-foreground">
            No services attached to this status page yet.
          </p>
        ) : (
          <ul className="mt-3 divide-y divide-border/60 rounded-lg border border-border/60">
            {page.services.map(svc => (
              <ServiceRow key={svc.name} service={svc} />
            ))}
          </ul>
        )}
      </section>

      <section className="mt-10">
        <h2 className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground">
          Active incidents
        </h2>
        {activeCount === 0 ? (
          <p className="mt-3 text-sm text-muted-foreground">No active incidents.</p>
        ) : (
          <ul className="mt-3 space-y-3">
            {page.services.flatMap(svc =>
              svc.activeIncidents.map((inc, idx) => (
                <li
                  key={`${svc.name}:${idx}`}
                  className="rounded-lg border border-border/60 px-4 py-3"
                >
                  <div className="flex items-center justify-between gap-3">
                    <SeverityBadge severity={inc.severity} />
                    <span className="font-mono text-[11px] tracking-wide uppercase text-muted-foreground">
                      {svc.name}
                    </span>
                  </div>
                  <p className="mt-2 text-sm text-foreground">{inc.title}</p>
                  <p className="mt-1 font-mono text-[11px] tracking-wide text-muted-foreground">
                    {inc.status} · opened {formatDistanceToNowStrict(new Date(inc.since), { addSuffix: true })}
                  </p>
                </li>
              )),
            )}
          </ul>
        )}
      </section>
    </div>
  )
}

function ServiceRow({ service }: { service: PublicServiceStatus }) {
  const tone = serviceStatusTone(service.status)
  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex items-center gap-3 min-w-0">
        <span className={cn('h-1.5 w-1.5 rounded-full shrink-0', TONE_DOT[tone])} />
        <span className="font-mono text-sm text-foreground truncate">{service.name}</span>
      </div>
      <div className="flex items-center gap-4 shrink-0">
        <span className="font-mono text-[10px] tracking-wide uppercase text-muted-foreground tabular-nums">
          {service.incidentsLast30Days} in 30d
        </span>
        <span className={cn('font-mono text-[11px] tracking-wide uppercase', TONE_TEXT[tone])}>
          {serviceStatusLabel(service.status)}
        </span>
      </div>
    </li>
  )
}

function Footer({ generatedAt }: { generatedAt?: string }) {
  return (
    <div className="mt-16 flex items-center justify-between">
      <p className="font-mono text-[10px] tracking-wide uppercase text-muted-foreground/60">
        Powered by Flare
      </p>
      {generatedAt && (
        <p className="font-mono text-[10px] tracking-wide uppercase text-muted-foreground/60 tabular-nums">
          {new Date(generatedAt).toISOString().slice(0, 16).replace('T', ' ')}Z
        </p>
      )}
    </div>
  )
}
