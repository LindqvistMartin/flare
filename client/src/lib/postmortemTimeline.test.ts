import { describe, expect, it } from 'vitest'
import { parsePostmortemTimeline } from './postmortemTimeline'

describe('parsePostmortemTimeline', () => {
  it('returns an empty array for malformed JSON', () => {
    expect(parsePostmortemTimeline('not json')).toEqual([])
  })

  it('returns an empty array when the root is not an array', () => {
    expect(parsePostmortemTimeline('{"At":"2026-05-23"}')).toEqual([])
  })

  it('normalizes PascalCase wire fields to camelCase', () => {
    const raw = JSON.stringify([
      { At: '2026-05-20T10:00:00Z', Type: 'Created', ActorId: null, Summary: 'Incident created' },
    ])
    expect(parsePostmortemTimeline(raw)).toEqual([
      { at: '2026-05-20T10:00:00Z', type: 'Created', actorId: null, summary: 'Incident created' },
    ])
  })

  it('also accepts already-camelCase entries (forward-compat)', () => {
    const raw = JSON.stringify([
      { at: '2026-05-20T10:00:00Z', type: 'Created', actorId: null, summary: 'Hello' },
    ])
    expect(parsePostmortemTimeline(raw)).toHaveLength(1)
  })

  it('drops entries with a missing or unparseable timestamp', () => {
    const raw = JSON.stringify([
      { Type: 'Created', Summary: 'no at field' },
      { At: 'not a date', Type: 'StatusChanged', Summary: 'bad date' },
      { At: '2026-05-20T10:00:00Z', Type: 'Resolved', Summary: 'ok' },
    ])
    expect(parsePostmortemTimeline(raw)).toHaveLength(1)
  })

  it('drops null elements and non-object entries', () => {
    const raw = JSON.stringify([
      null,
      'string',
      42,
      { At: '2026-05-20T10:00:00Z', Type: 'Created', Summary: 'ok' },
    ])
    expect(parsePostmortemTimeline(raw)).toHaveLength(1)
  })

  it('falls back to an empty summary when the field is missing', () => {
    const raw = JSON.stringify([{ At: '2026-05-20T10:00:00Z', Type: 'X' }])
    expect(parsePostmortemTimeline(raw)).toEqual([
      { at: '2026-05-20T10:00:00Z', type: 'X', actorId: null, summary: '' },
    ])
  })
})
