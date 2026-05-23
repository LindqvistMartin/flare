import type { ActionItem, ActionItemStatus } from '@/api/types'

export type ActionItemFilterKey = 'All' | 'Open' | 'InProgress' | 'Done' | 'Overdue'

export const ACTION_ITEM_FILTERS: readonly ActionItemFilterKey[] = [
  'All',
  'Open',
  'InProgress',
  'Done',
  'Overdue',
]

// YYYY-MM-DD UTC. ActionItem.dueDate ships as an ISO date string (no time);
// comparing as lexicographic strings is equivalent to date comparison when both
// sides are in the same canonical format, and avoids dragging timezone math
// into the client.
function utcDateString(now: Date): string {
  return now.toISOString().slice(0, 10)
}

export function isActionItemOverdue(item: ActionItem, now: Date = new Date()): boolean {
  if (item.status === 'Done') return false
  if (item.dueDate === null) return false
  return item.dueDate < utcDateString(now)
}

// Pure filter for the tracker page. `Overdue` excludes `Done` because a done
// item with a past due date is not a current liability — it shipped, late but
// shipped. The tabs are mutually exclusive; `All` returns everything verbatim.
export function filterActionItems(
  items: ActionItem[],
  filter: ActionItemFilterKey,
  now: Date = new Date(),
): ActionItem[] {
  switch (filter) {
    case 'All':
      return items
    case 'Overdue':
      return items.filter(i => isActionItemOverdue(i, now))
    default:
      return items.filter(i => i.status === (filter as ActionItemStatus))
  }
}

export function countByFilter(
  items: ActionItem[],
  now: Date = new Date(),
): Record<ActionItemFilterKey, number> {
  return {
    All: items.length,
    Open: items.filter(i => i.status === 'Open').length,
    InProgress: items.filter(i => i.status === 'InProgress').length,
    Done: items.filter(i => i.status === 'Done').length,
    Overdue: items.filter(i => isActionItemOverdue(i, now)).length,
  }
}
