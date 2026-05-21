import { useState } from 'react'
import { ChevronDown, ChevronUp, BookOpen } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

interface RunbookSidebarProps {
  serviceName: string | undefined
  runbookBody: string | undefined
  loading?: boolean
}

export function RunbookSidebar({ serviceName, runbookBody, loading }: RunbookSidebarProps) {
  const [open, setOpen] = useState(true)
  const hasRunbook = typeof runbookBody === 'string' && runbookBody.trim().length > 0

  return (
    <Card className="h-fit">
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <BookOpen className="h-3 w-3 text-muted-foreground" aria-hidden />
            <p className="font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground">
              Runbook
            </p>
          </div>
          <Button
            type="button"
            size="icon"
            variant="ghost"
            className="h-6 w-6 text-muted-foreground hover:text-foreground"
            aria-label={open ? 'Collapse runbook' : 'Expand runbook'}
            aria-expanded={open}
            onClick={() => setOpen(o => !o)}
          >
            {open ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
          </Button>
        </div>

        {serviceName && (
          <p className="font-mono text-[11px] text-muted-foreground truncate" title={serviceName}>
            {serviceName}
          </p>
        )}

        {open && (
          <div className="border-t border-border pt-3">
            {loading && (
              <p className="font-mono text-[11px] text-muted-foreground">Loading runbook…</p>
            )}
            {!loading && !hasRunbook && (
              <div className="space-y-1">
                <p className="text-sm font-medium text-foreground">No runbook yet</p>
                <p className="font-mono text-[11px] text-muted-foreground">
                  Document mitigation steps in the service profile to surface them here.
                </p>
              </div>
            )}
            {!loading && hasRunbook && (
              <div
                className={cn(
                  'prose prose-sm max-w-none',
                  'dark:prose-invert',
                  'prose-headings:font-medium prose-headings:tracking-tight',
                  'prose-p:text-foreground prose-p:leading-relaxed',
                  'prose-code:text-foreground prose-code:bg-muted prose-code:px-1 prose-code:py-0.5 prose-code:rounded-sm prose-code:font-mono prose-code:text-[12px]',
                  'prose-a:text-amber-600 dark:prose-a:text-amber-400',
                )}
              >
                <ReactMarkdown remarkPlugins={[remarkGfm]}>{runbookBody!}</ReactMarkdown>
              </div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
