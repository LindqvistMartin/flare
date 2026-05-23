import { cn } from '@/lib/utils'
import { postmortemStatusChipClass } from '@/lib/postmortemStatus'
import type { PostmortemStatus } from '@/api/types'

interface PostmortemStatusChipProps {
  status: PostmortemStatus
  className?: string
}

export function PostmortemStatusChip({ status, className }: PostmortemStatusChipProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-sm border px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wide',
        postmortemStatusChipClass(status),
        className,
      )}
    >
      {status}
    </span>
  )
}
