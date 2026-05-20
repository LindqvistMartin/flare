import { describe, it, expect } from 'vitest'
import { bucketIncidentsToMttrTrend, computeMttrTrendDelta } from '@/lib/mttrTrend'
import type { Incident } from '@/api/types'

const NOW = new Date('2026-05-20T12:00:00.000Z')

function makeIncident(partial: Partial<Incident>): Incident {
  return {
    id: 'i1',
    serviceId: 's1',
    title: 't',
    severity: 'Sev2',
    status: 'Resolved',
    commanderId: null,
    communicatorId: null,
    responderId: null,
    createdAt: '2026-05-20T10:00:00.000Z',
    acknowledgedAt: null,
    resolvedAt: '2026-05-20T11:00:00.000Z',
    closedAt: null,
    ...partial,
  }
}

describe('bucketIncidentsToMttrTrend', () => {
  it('returns 30 contiguous days ending today', () => {
    const trend = bucketIncidentsToMttrTrend([], NOW)
    expect(trend).toHaveLength(30)
    expect(trend[0].day).toBe('2026-04-21')
    expect(trend[29].day).toBe('2026-05-20')
  })

  it('places resolved incidents in the bucket of their resolvedAt date (UTC)', () => {
    const trend = bucketIncidentsToMttrTrend([
      makeIncident({
        createdAt: '2026-05-19T08:00:00.000Z',
        resolvedAt: '2026-05-19T09:00:00.000Z',
      }),
    ], NOW)
    const day = trend.find(p => p.day === '2026-05-19')
    expect(day?.avgMttrMs).toBe(3_600_000)
  })

  it('averages multiple incidents in the same bucket', () => {
    const trend = bucketIncidentsToMttrTrend([
      makeIncident({
        id: 'a',
        createdAt: '2026-05-20T10:00:00.000Z',
        resolvedAt: '2026-05-20T11:00:00.000Z',
      }),
      makeIncident({
        id: 'b',
        createdAt: '2026-05-20T10:00:00.000Z',
        resolvedAt: '2026-05-20T13:00:00.000Z',
      }),
    ], NOW)
    const today = trend.find(p => p.day === '2026-05-20')
    expect(today?.avgMttrMs).toBe(7_200_000) // (1h + 3h) / 2 = 2h
  })

  it('returns null for days with no incidents', () => {
    const trend = bucketIncidentsToMttrTrend([], NOW)
    expect(trend.every(p => p.avgMttrMs === null)).toBe(true)
  })

  it('ignores incidents with missing timestamps', () => {
    const trend = bucketIncidentsToMttrTrend([
      makeIncident({ resolvedAt: null }),
    ], NOW)
    expect(trend.every(p => p.avgMttrMs === null)).toBe(true)
  })

  it('drops negative MTTR (resolvedAt before createdAt indicates corrupt data)', () => {
    const trend = bucketIncidentsToMttrTrend([
      makeIncident({
        createdAt: '2026-05-20T11:00:00.000Z',
        resolvedAt: '2026-05-20T10:00:00.000Z',
      }),
    ], NOW)
    const today = trend.find(p => p.day === '2026-05-20')
    expect(today?.avgMttrMs).toBeNull()
  })
})

describe('computeMttrTrendDelta', () => {
  it('returns null when prior window is empty', () => {
    const points = bucketIncidentsToMttrTrend([
      makeIncident({
        createdAt: '2026-05-20T10:00:00.000Z',
        resolvedAt: '2026-05-20T11:00:00.000Z',
      }),
    ], NOW)
    expect(computeMttrTrendDelta(points)).toBeNull()
  })

  it('computes a positive delta when recent MTTR is higher than prior MTTR', () => {
    const priorIncident = makeIncident({
      id: 'old',
      createdAt: '2026-04-22T10:00:00.000Z',
      resolvedAt: '2026-04-22T11:00:00.000Z', // 1h
    })
    const recentIncident = makeIncident({
      id: 'new',
      createdAt: '2026-05-18T10:00:00.000Z',
      resolvedAt: '2026-05-18T13:00:00.000Z', // 3h
    })
    const points = bucketIncidentsToMttrTrend([priorIncident, recentIncident], NOW)
    const delta = computeMttrTrendDelta(points)
    expect(delta).not.toBeNull()
    expect(delta!).toBeGreaterThan(0)
  })
})
