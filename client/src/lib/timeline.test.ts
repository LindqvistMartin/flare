import { describe, it, expect } from 'vitest'
import {
  iconForEventType,
  summarizeEvent,
  shortActor,
} from '@/lib/timeline'
import type { IncidentEvent } from '@/api/types'

function makeEvent(partial: Partial<IncidentEvent>): IncidentEvent {
  return {
    id: 'e1',
    incidentId: 'i1',
    type: 'Created',
    actorId: null,
    payload: '{}',
    createdAt: '2026-05-21T10:00:00.000Z',
    ...partial,
  }
}

describe('iconForEventType', () => {
  it('returns a component for every known event type', () => {
    // Mirrors IncidentEventType in src/Flare.Core/Entities/IncidentEvent.cs.
    const types = [
      'Created',
      'StatusChanged',
      'RoleAssigned',
      'CommentAdded',
      'SeverityChanged',
      'NotificationDispatched',
      'WebhookReceived',
    ]
    for (const t of types) {
      expect(typeof iconForEventType(t)).toBe('object')
    }
  })

  it('falls back to a generic icon for unknown types', () => {
    expect(iconForEventType('SomethingNew')).toBeDefined()
  })
})

describe('summarizeEvent', () => {
  it('formats Created events', () => {
    expect(summarizeEvent(makeEvent({ type: 'Created' }))).toBe('Incident opened')
  })

  it('reads StatusChanged target case-insensitively (PascalCase from outbox)', () => {
    expect(
      summarizeEvent(makeEvent({ type: 'StatusChanged', payload: '{"To":"Resolved"}' })),
    ).toBe('Status → Resolved')
    expect(
      summarizeEvent(makeEvent({ type: 'StatusChanged', payload: '{"to":"Investigating"}' })),
    ).toBe('Status → Investigating')
  })

  it('renders a fallback when StatusChanged has no target', () => {
    expect(
      summarizeEvent(makeEvent({ type: 'StatusChanged', payload: '{}' })),
    ).toBe('Status changed')
  })

  it('renders comment text inline, trimmed and capped', () => {
    const long = 'a'.repeat(200)
    const out = summarizeEvent(makeEvent({ type: 'CommentAdded', payload: JSON.stringify({ comment: long }) }))
    expect(out.endsWith('…')).toBe(true)
    expect(out.length).toBe(140)
  })

  it('collapses whitespace in comments', () => {
    expect(
      summarizeEvent(makeEvent({ type: 'CommentAdded', payload: '{"comment":"line one\\n\\nline two"}' })),
    ).toBe('line one line two')
  })

  it('does not throw on malformed payload', () => {
    expect(() =>
      summarizeEvent(makeEvent({ type: 'StatusChanged', payload: '{not json' })),
    ).not.toThrow()
    expect(summarizeEvent(makeEvent({ type: 'CommentAdded', payload: '' }))).toBe('Comment added')
  })

  it('formats role assignment with truncated actor id', () => {
    expect(
      summarizeEvent(
        makeEvent({
          type: 'RoleAssigned',
          payload: JSON.stringify({ role: 'Commander', userId: 'd1234567-89ab-cdef-0123-456789abcdef' }),
        }),
      ),
    ).toBe('Commander assigned to d1234567')
  })

  it('falls back to type string for unknown event types', () => {
    expect(summarizeEvent(makeEvent({ type: 'SomethingNew' }))).toBe('SomethingNew')
  })
})

describe('shortActor', () => {
  it('returns "system" for null', () => {
    expect(shortActor(null)).toBe('system')
  })

  it('truncates GUIDs to 8 chars', () => {
    expect(shortActor('d1234567-89ab-cdef-0123-456789abcdef')).toBe('d1234567')
  })
})

describe('summarizeEvent — WebhookReceived', () => {
  it('renders source when present', () => {
    expect(
      summarizeEvent({
        id: 'e1',
        incidentId: 'i1',
        type: 'WebhookReceived',
        actorId: null,
        payload: JSON.stringify({ source: 'prometheus' }),
        createdAt: '2026-05-21T10:00:00.000Z',
      }),
    ).toBe('Ingested from prometheus')
  })
})
