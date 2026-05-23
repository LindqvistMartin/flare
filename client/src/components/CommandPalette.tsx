import { useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { Command } from 'cmdk'
import { AlertTriangle, CheckSquare, Search, SunMoon } from 'lucide-react'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import { useCommandPalette } from '@/contexts/CommandPaletteContext'
import { useIncidents } from '@/api/hooks/useIncidents'
import { useTheme } from '@/contexts/ThemeContext'
import { SeverityBadge } from '@/components/SeverityBadge'

const PALETTE_INCIDENT_LIMIT = 20

export function CommandPalette() {
  const { open, setOpen } = useCommandPalette()
  const navigate = useNavigate()
  const { toggleTheme } = useTheme()
  const incidents = useIncidents()

  // Limit the list we render — palette stays snappy on instances with many
  // historical incidents. cmdk's own fuzzy filter handles the typed query.
  const visibleIncidents = useMemo(
    () =>
      [...incidents.data]
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
        .slice(0, PALETTE_INCIDENT_LIMIT),
    [incidents.data],
  )

  const close = () => setOpen(false)
  const go = (path: string) => {
    close()
    navigate(path)
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent className="max-w-xl gap-0 p-0 sm:rounded-md">
        {/* DialogTitle is required for Radix Dialog a11y; visually hidden so
            the palette's own input is the obvious focus target. */}
        <DialogTitle className="sr-only">Command palette</DialogTitle>
        <Command label="Command palette" className="overflow-hidden">
          <div className="flex items-center gap-2 border-b border-border px-3">
            <Search className="h-3.5 w-3.5 text-muted-foreground" aria-hidden />
            <Command.Input
              aria-label="Search commands and incidents"
              placeholder="Search incidents, navigate, toggle…"
              className="flex h-11 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
            />
          </div>

          <Command.List className="max-h-80 overflow-y-auto p-1">
            <Command.Empty className="px-3 py-6 text-center font-mono text-[11px] text-muted-foreground">
              No results.
            </Command.Empty>

            <Command.Group
              heading="Navigate"
              className="px-1 py-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground"
            >
              <PaletteAction
                icon={<CheckSquare className="h-3.5 w-3.5" aria-hidden />}
                title="Open action items"
                onSelect={() => go('/action-items')}
              />
              <PaletteAction
                icon={<SunMoon className="h-3.5 w-3.5" aria-hidden />}
                title="Toggle dark mode"
                onSelect={() => {
                  toggleTheme()
                  close()
                }}
              />
            </Command.Group>

            {visibleIncidents.length > 0 && (
              <Command.Group
                heading="Incidents"
                className="px-1 py-1 font-mono text-[10px] tracking-[0.18em] uppercase text-muted-foreground"
              >
                {visibleIncidents.map(inc => (
                  <Command.Item
                    key={inc.id}
                    value={`${inc.title} ${inc.id} ${inc.severity} ${inc.status}`}
                    onSelect={() => go(`/incidents/${inc.id}`)}
                    className="flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm aria-selected:bg-muted"
                  >
                    <AlertTriangle className="h-3.5 w-3.5 text-muted-foreground" aria-hidden />
                    <span className="flex-1 truncate text-foreground">{inc.title}</span>
                    <SeverityBadge severity={inc.severity} className="shrink-0" />
                  </Command.Item>
                ))}
              </Command.Group>
            )}
          </Command.List>
        </Command>
      </DialogContent>
    </Dialog>
  )
}

interface PaletteActionProps {
  icon: React.ReactNode
  title: string
  hint?: string
  onSelect: () => void
}

function PaletteAction({ icon, title, hint, onSelect }: PaletteActionProps) {
  return (
    <Command.Item
      onSelect={onSelect}
      value={title}
      className="flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm aria-selected:bg-muted"
    >
      <span className="text-muted-foreground">{icon}</span>
      <span className="flex-1 text-foreground">{title}</span>
      {hint && (
        <span className="font-mono text-[10px] text-muted-foreground">{hint}</span>
      )}
    </Command.Item>
  )
}
