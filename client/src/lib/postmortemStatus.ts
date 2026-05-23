import type { PostmortemStatus } from '@/api/types'

// Chip class for postmortem Draft / Published status. Mirrors the StatusChip
// pattern used for incident status but on the postmortem axis (two states only).
// Draft → amber, the same warning hue that the rest of the app uses for
// "needs human input"; Published → emerald, matching Resolved on the incident
// timeline because the postmortem reaches its terminal state at publish time.
export function postmortemStatusChipClass(status: PostmortemStatus): string {
  switch (status) {
    case 'Draft':
      return 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-300'
    case 'Published':
      return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-300'
  }
}

export function isPostmortemEditable(status: PostmortemStatus): boolean {
  return status === 'Draft'
}
