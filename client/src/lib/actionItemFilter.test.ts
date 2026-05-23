import { describe, expect, it } from 'vitest'
import {
  countByFilter,
  filterActionItems,
  isActionItemOverdue,
  type ActionItemFilterKey,
} from './actionItemFilter'
import type { ActionItem } from '@/api/types'

const now = new Date('2026-05-23T12:00:00Z')

function item(over: Partial<ActionItem>): ActionItem {
  return {
    id: crypto.randomUUID(),
    postmortemId: crypto.randomUUID(),
    title: 'an item',
    ownerId: null,
    dueDate: null,
    status: 'Open',
    createdAt: '2026-05-20T00:00:00Z',
    ...over,
  }
}

describe('isActionItemOverdue', () => {
  it('flags an open item past its due date', () => {
    expect(isActionItemOverdue(item({ status: 'Open', dueDate: '2026-05-20' }), now)).toBe(true)
  })

  it('ignores items already done even if past due', () => {
    expect(isActionItemOverdue(item({ status: 'Done', dueDate: '2026-05-20' }), now)).toBe(false)
  })

  it('ignores items without a due date', () => {
    expect(isActionItemOverdue(item({ status: 'Open', dueDate: null }), now)).toBe(false)
  })

  it('treats a due date in the future as not overdue', () => {
    expect(isActionItemOverdue(item({ status: 'Open', dueDate: '2026-06-01' }), now)).toBe(false)
  })

  it('treats a due date equal to today as not overdue (grace until end of day)', () => {
    expect(isActionItemOverdue(item({ status: 'Open', dueDate: '2026-05-23' }), now)).toBe(false)
  })
})

describe('filterActionItems', () => {
  const items: ActionItem[] = [
    item({ status: 'Open', dueDate: '2026-05-20' }),
    item({ status: 'InProgress', dueDate: '2026-06-01' }),
    item({ status: 'Done', dueDate: '2026-05-20' }),
    item({ status: 'Open', dueDate: null }),
  ]

  it.each<[ActionItemFilterKey, number]>([
    ['All', 4],
    ['Open', 2],
    ['InProgress', 1],
    ['Done', 1],
    ['Overdue', 1],
  ])('selects %s correctly', (key, expected) => {
    expect(filterActionItems(items, key, now)).toHaveLength(expected)
  })
})

describe('countByFilter', () => {
  it('returns a count per filter key', () => {
    const items: ActionItem[] = [
      item({ status: 'Open', dueDate: '2026-05-20' }),
      item({ status: 'Open', dueDate: null }),
      item({ status: 'InProgress', dueDate: '2026-06-01' }),
      item({ status: 'Done', dueDate: '2026-05-10' }),
    ]
    expect(countByFilter(items, now)).toEqual({
      All: 4,
      Open: 2,
      InProgress: 1,
      Done: 1,
      Overdue: 1,
    })
  })
})
