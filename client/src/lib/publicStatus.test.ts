import { describe, it, expect } from 'vitest'
import {
  countActiveIncidents,
  overallStatusLabel,
  overallStatusTone,
  serviceStatusLabel,
  serviceStatusTone,
} from '@/lib/publicStatus'
import type { PublicServiceStatus } from '@/api/publicTypes'

function makeService(partial: Partial<PublicServiceStatus> = {}): PublicServiceStatus {
  return {
    name: 'svc',
    status: 'operational',
    incidentsLast30Days: 0,
    activeIncidents: [],
    ...partial,
  }
}

describe('overallStatusLabel', () => {
  it('produces human-readable labels for each known status', () => {
    expect(overallStatusLabel('operational')).toBe('All systems operational')
    expect(overallStatusLabel('degraded')).toBe('Degraded performance')
    expect(overallStatusLabel('major')).toBe('Major outage')
    expect(overallStatusLabel('unknown')).toBe('Status unknown')
  })
})

describe('overallStatusTone', () => {
  it('maps degraded to warning and major to alert', () => {
    expect(overallStatusTone('operational')).toBe('positive')
    expect(overallStatusTone('degraded')).toBe('warning')
    expect(overallStatusTone('major')).toBe('alert')
    expect(overallStatusTone('unknown')).toBe('muted')
  })
})

describe('serviceStatusLabel / tone', () => {
  it('uses shorter labels for per-row chips', () => {
    expect(serviceStatusLabel('operational')).toBe('Operational')
    expect(serviceStatusLabel('major')).toBe('Outage')
  })

  it('shares the tone palette with the overall status', () => {
    expect(serviceStatusTone('major')).toBe(overallStatusTone('major'))
    expect(serviceStatusTone('operational')).toBe(overallStatusTone('operational'))
  })
})

describe('countActiveIncidents', () => {
  it('returns zero for an empty service list', () => {
    expect(countActiveIncidents([])).toBe(0)
  })

  it('sums active incidents across services', () => {
    const services: PublicServiceStatus[] = [
      makeService({
        activeIncidents: [
          { title: 'a', severity: 'Sev2', status: 'Investigating', since: '2026-05-20T00:00:00Z' },
          { title: 'b', severity: 'Sev3', status: 'Identified', since: '2026-05-20T01:00:00Z' },
        ],
      }),
      makeService({
        activeIncidents: [
          { title: 'c', severity: 'Sev1', status: 'Triggered', since: '2026-05-20T02:00:00Z' },
        ],
      }),
      makeService(),
    ]
    expect(countActiveIncidents(services)).toBe(3)
  })
})
