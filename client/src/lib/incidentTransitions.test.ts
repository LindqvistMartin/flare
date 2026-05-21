import { describe, it, expect } from 'vitest'
import { allowedNextStatuses, isTerminalStatus } from '@/lib/incidentTransitions'
import type { IncidentStatus } from '@/api/types'

describe('allowedNextStatuses', () => {
  it.each<[IncidentStatus, IncidentStatus[]]>([
    ['Triggered',     ['Investigating', 'Identified', 'Resolved']],
    ['Investigating', ['Identified', 'Resolved']],
    ['Identified',    ['Monitoring', 'Resolved']],
    ['Monitoring',    ['Resolved']],
    ['Resolved',      ['Closed']],
    ['Closed',        []],
  ])('returns the exact allowed set for %s', (from, expected) => {
    expect(allowedNextStatuses(from)).toEqual(expected)
  })

  it('never lists the current status as a self-transition', () => {
    const statuses: IncidentStatus[] = [
      'Triggered', 'Investigating', 'Identified', 'Monitoring', 'Resolved', 'Closed',
    ]
    for (const s of statuses) {
      expect(allowedNextStatuses(s)).not.toContain(s)
    }
  })
})

describe('isTerminalStatus', () => {
  it('flags only Closed as terminal', () => {
    expect(isTerminalStatus('Closed')).toBe(true)
    expect(isTerminalStatus('Resolved')).toBe(false)
    expect(isTerminalStatus('Triggered')).toBe(false)
  })
})
