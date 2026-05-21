import type { LucideIcon } from 'lucide-react'
import {
  AlertCircle,
  ArrowRightCircle,
  MessageSquare,
  Radio,
  ShieldAlert,
  UserCheck,
  Webhook,
  Activity,
} from 'lucide-react'
import type { IncidentEvent } from '@/api/types'

// Known event types match IncidentEventType in src/Flare.Core/Entities/IncidentEvent.cs.
// Unknown strings fall back to a generic Activity icon so a future backend type
// landing before a client bump renders sensibly instead of disappearing.
export function iconForEventType(type: string): LucideIcon {
  switch (type) {
    case 'Created':                return AlertCircle
    case 'StatusChanged':          return ArrowRightCircle
    case 'RoleAssigned':           return UserCheck
    case 'CommentAdded':           return MessageSquare
    case 'SeverityChanged':        return ShieldAlert
    case 'NotificationDispatched': return Radio
    case 'WebhookReceived':        return Webhook
    default:                       return Activity
  }
}

// PascalCase tolerant read — outbox payloads ship verbatim with the casing used
// by JsonSerializer.Serialize(anonObj), which defaults to PascalCase. The
// backend PostmortemDraftBuilder shipped a hotfix for the same drift; the
// client follows the same case-insensitive policy.
function readString(record: Record<string, unknown>, key: string): string | null {
  const target = key.toLowerCase()
  for (const [k, v] of Object.entries(record)) {
    if (k.toLowerCase() === target && typeof v === 'string') return v
  }
  return null
}

function parsePayload(raw: string): Record<string, unknown> | null {
  if (!raw) return null
  try {
    const parsed: unknown = JSON.parse(raw)
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

// One-line, human-readable description of the event for the timeline feed.
// Pure — accepts only the event itself, no React, no formatting helpers.
export function summarizeEvent(event: IncidentEvent): string {
  const payload = parsePayload(event.payload)

  switch (event.type) {
    case 'Created':
      return 'Incident opened'

    case 'StatusChanged': {
      const to = payload ? readString(payload, 'to') : null
      return to ? `Status → ${to}` : 'Status changed'
    }

    case 'RoleAssigned': {
      const role = payload ? readString(payload, 'role') : null
      const userId = payload ? readString(payload, 'userId') : null
      if (role && userId) return `${role} assigned to ${userId.slice(0, 8)}`
      if (role) return `${role} unassigned`
      return 'Role updated'
    }

    case 'CommentAdded': {
      // `comment` is the canonical key. CommentComposer writes it; the backend
      // PostmortemDraftBuilder reads the same key case-insensitively.
      const body = payload ? readString(payload, 'comment') : null
      if (!body) return 'Comment added'
      // Single-line cap so a multi-paragraph comment does not bloat the row.
      const oneLine = body.replace(/\s+/g, ' ').trim()
      return oneLine.length > 140 ? `${oneLine.slice(0, 139)}…` : oneLine
    }

    case 'SeverityChanged': {
      const to = payload ? readString(payload, 'to') ?? readString(payload, 'severity') : null
      return to ? `Severity → ${to}` : 'Severity changed'
    }

    case 'NotificationDispatched': {
      const channel = payload ? readString(payload, 'channel') : null
      return channel ? `Notification dispatched to ${channel}` : 'Notification dispatched'
    }

    case 'WebhookReceived': {
      const source = payload ? readString(payload, 'source') : null
      return source ? `Ingested from ${source}` : 'Ingested from webhook'
    }

    default:
      return event.type
  }
}

export function shortActor(actorId: string | null): string {
  if (actorId === null) return 'system'
  return actorId.slice(0, 8)
}
