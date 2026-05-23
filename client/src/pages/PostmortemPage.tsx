import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import { HubConnectionState } from '@microsoft/signalr'
import { ArrowLeft, FileText, Send, Sparkles } from 'lucide-react'
import { AppShell, type ConnectionStatus } from '@/components/AppShell'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { SeverityBadge } from '@/components/SeverityBadge'
import { PostmortemStatusChip } from '@/components/PostmortemStatusChip'
import { PostmortemTimeline } from '@/components/PostmortemTimeline'
import { useIncident } from '@/api/hooks/useIncidents'
import { useService } from '@/api/hooks/useServices'
import { useActionItems } from '@/api/hooks/useActionItems'
import {
  useGeneratePostmortem,
  usePostmortem,
  usePublishPostmortem,
} from '@/api/hooks/usePostmortem'
import { useFlareHub } from '@/hooks/useFlareHub'
import { isPostmortemEditable } from '@/lib/postmortemStatus'
import { formatTimestampUtc } from '@/lib/format'
import type { PostmortemTimelineEntry } from '@/api/types'

function mapConnection(state: HubConnectionState): ConnectionStatus {
  if (state === HubConnectionState.Connected) return 'connected'
  if (state === HubConnectionState.Connecting || state === HubConnectionState.Reconnecting) {
    return 'connecting'
  }
  return 'disconnected'
}

const VALID_GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function parseTimeline(raw: string): PostmortemTimelineEntry[] {
  try {
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? (parsed as PostmortemTimelineEntry[]) : []
  } catch {
    return []
  }
}

export function PostmortemPage() {
  const { id = '' } = useParams<{ id: string }>()
  const guidLike = VALID_GUID_RE.test(id)

  const { connectionState } = useFlareHub('incident', id)
  const incidentQuery = useIncident(guidLike ? id : null)
  const incident = incidentQuery.data
  const postmortemQuery = usePostmortem(guidLike ? id : null)
  const postmortem = postmortemQuery.data ?? null
  const serviceQuery = useService(incident?.serviceId ?? null)
  const actionItemsQuery = useActionItems()
  const generateMut = useGeneratePostmortem(id)
  const publishMut = usePublishPostmortem(id)

  // Subtle "just generated" highlight on the Impact card. TanStack's
  // submittedAt is a millisecond timestamp that updates on each successful
  // mutation submission; using it as a React key remounts the highlighted
  // div and replays the keyframe. No setState-in-effect dance required.
  const highlightKey = generateMut.submittedAt

  const timeline = useMemo(
    () => (postmortem ? parseTimeline(postmortem.timeline) : []),
    [postmortem],
  )
  const itemsForPostmortem = useMemo(
    () => (postmortem ? actionItemsQuery.data.filter(i => i.postmortemId === postmortem.id) : []),
    [actionItemsQuery.data, postmortem],
  )

  const connectionStatus = mapConnection(connectionState)
  const loading = !postmortemQuery.isFetched || !incidentQuery.isFetched

  if (!guidLike) {
    return (
      <AppShell connectionStatus={connectionStatus}>
        <NotFoundCard />
      </AppShell>
    )
  }

  return (
    <AppShell connectionStatus={connectionStatus}>
      <div className="space-y-6">
        <Link
          to={`/incidents/${id}`}
          className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-3 w-3" aria-hidden />
          Incident
        </Link>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <div className="flex items-center gap-3">
              {incident && <SeverityBadge severity={incident.severity} />}
              <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
                Postmortem
              </p>
              {postmortem && <PostmortemStatusChip status={postmortem.status} />}
            </div>
            <h1 className="max-w-2xl text-lg font-semibold tracking-tight text-foreground break-words">
              {incident?.title ?? 'Loading…'}
            </h1>
            {postmortem && (
              <p className="font-mono text-[11px] text-muted-foreground">
                {serviceQuery.data?.name ?? '…'} · drafted {formatTimestampUtc(postmortem.createdAt)}
                {postmortem.publishedAt && (
                  <> · published {formatTimestampUtc(postmortem.publishedAt)}</>
                )}
              </p>
            )}
          </div>

          <div className="flex items-center gap-2">
            {postmortem && isPostmortemEditable(postmortem.status) && (
              <Button
                size="sm"
                variant="secondary"
                onClick={() => generateMut.mutate()}
                disabled={generateMut.isPending}
                className="font-mono text-[10px] uppercase tracking-wide"
              >
                <Sparkles className="mr-1.5 h-3 w-3" aria-hidden />
                Regenerate draft
              </Button>
            )}
            {postmortem && isPostmortemEditable(postmortem.status) && (
              <Button
                size="sm"
                onClick={() => publishMut.mutate()}
                disabled={publishMut.isPending}
                className="font-mono text-[10px] uppercase tracking-wide"
              >
                <Send className="mr-1.5 h-3 w-3" aria-hidden />
                Publish
              </Button>
            )}
          </div>
        </div>

        {loading && !postmortem ? (
          <LoadingSkeleton />
        ) : postmortem === null ? (
          <EmptyState
            onGenerate={() => generateMut.mutate()}
            pending={generateMut.isPending}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <Section title="Impact" highlightKey={highlightKey} span={2}>
              <pre className="whitespace-pre-wrap break-words font-mono text-[12px] leading-relaxed text-foreground">
                {postmortem.impact || '—'}
              </pre>
            </Section>

            <Section title="Timeline" span={2}>
              <PostmortemTimeline entries={timeline} />
            </Section>

            <Section title="Contributing factors">
              <ContributingFactorsPlaceholder />
            </Section>

            <Section title="Action items">
              {itemsForPostmortem.length === 0 ? (
                <p className="font-mono text-[11px] text-muted-foreground">
                  No action items yet. Add them on the{' '}
                  <Link to="/action-items" className="underline-offset-2 hover:underline">
                    tracker
                  </Link>
                  .
                </p>
              ) : (
                <ul className="space-y-1.5">
                  {itemsForPostmortem.map(item => (
                    <li
                      key={item.id}
                      className="flex items-baseline justify-between gap-3 font-mono text-[11px]"
                    >
                      <span className="text-foreground">{item.title}</span>
                      <span className="text-muted-foreground tabular-nums uppercase tracking-wide">
                        {item.status}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </Section>
          </div>
        )}
      </div>
    </AppShell>
  )
}

interface SectionProps {
  title: string
  children: React.ReactNode
  highlightKey?: number
  span?: 1 | 2
}

function Section({ title, children, highlightKey, span = 1 }: SectionProps) {
  const className =
    span === 2 ? 'lg:col-span-2' : ''
  return (
    <Card className={className}>
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2">
          <FileText className="h-3 w-3 text-muted-foreground" aria-hidden />
          <h2 className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
            {title}
          </h2>
        </div>
        <div
          key={highlightKey}
          data-highlight={highlightKey !== undefined && highlightKey > 0 ? 'true' : undefined}
          className="data-[highlight=true]:animate-pm-highlight"
        >
          {children}
        </div>
      </CardContent>
    </Card>
  )
}

function ContributingFactorsPlaceholder() {
  // RootCause is a non-derived field in the domain entity (PostmortemDraftBuilder
  // ships an empty string). A hand-editable input lands when the backend gains
  // a PATCH endpoint; until then the placeholder makes the gap honest instead
  // of pretending the field is captured.
  return (
    <p className="font-mono text-[11px] text-muted-foreground">
      Contributing factors are not auto-derived from the timeline. The hand-editable form
      lands when the backend gets a postmortem PATCH endpoint.
    </p>
  )
}

interface EmptyStateProps {
  onGenerate: () => void
  pending: boolean
}

function EmptyState({ onGenerate, pending }: EmptyStateProps) {
  return (
    <Card>
      <CardContent className="space-y-3 p-8 text-center">
        <p className="text-sm font-medium text-foreground">No postmortem yet</p>
        <p className="mx-auto max-w-md font-mono text-[11px] text-muted-foreground">
          Generate a draft from the incident timeline. Impact, status changes, comments, and
          role assignments are stitched into a markdown-friendly summary.
        </p>
        <div className="pt-2">
          <Button
            size="sm"
            onClick={onGenerate}
            disabled={pending}
            className="font-mono text-[10px] uppercase tracking-wide"
          >
            <Sparkles className="mr-1.5 h-3 w-3" aria-hidden />
            Generate draft
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function LoadingSkeleton() {
  return (
    <Card>
      <CardContent className="space-y-3 p-4">
        <div className="h-3 w-24 animate-pulse rounded-sm bg-muted" />
        <div className="h-5 w-full animate-pulse rounded-sm bg-muted" />
        <div className="h-5 w-3/4 animate-pulse rounded-sm bg-muted" />
        <div className="h-5 w-2/3 animate-pulse rounded-sm bg-muted" />
      </CardContent>
    </Card>
  )
}

function NotFoundCard() {
  return (
    <div className="space-y-6">
      <Link
        to="/dashboard"
        className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3 w-3" aria-hidden />
        Dashboard
      </Link>
      <Card>
        <CardContent className="space-y-3 p-8 text-center">
          <p className="text-sm font-medium text-foreground">Incident not found</p>
          <p className="font-mono text-[11px] text-muted-foreground">
            The id in the URL did not match an incident.
          </p>
        </CardContent>
      </Card>
    </div>
  )
}

