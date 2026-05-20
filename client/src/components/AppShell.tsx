import { type ReactNode } from 'react'
import { Sun, Moon, Terminal, Flame } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useTheme } from '@/contexts/ThemeContext'
import { cn } from '@/lib/utils'

export type ConnectionStatus = 'connected' | 'connecting' | 'disconnected'

interface ConnectionStatusDotProps {
  status: ConnectionStatus
}

function ConnectionStatusDot({ status }: ConnectionStatusDotProps) {
  return (
    <div
      className="relative flex h-5 w-5 items-center justify-center"
      role="img"
      aria-label={`SignalR ${status}`}
    >
      <div
        className={cn('h-1.5 w-1.5 rounded-full', {
          'bg-emerald-500': status === 'connected',
          'bg-amber-400': status === 'connecting',
          'bg-red-500': status === 'disconnected',
        })}
      />
      {status === 'connected' && (
        <div className="absolute h-1.5 w-1.5 rounded-full bg-emerald-500 animate-ping opacity-60" />
      )}
      {status === 'connecting' && (
        <div className="absolute h-1.5 w-1.5 rounded-full bg-amber-400 animate-pulse opacity-60" />
      )}
    </div>
  )
}

const NAV: { to: string; label: string }[] = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/services', label: 'Services' },
  { to: '/action-items', label: 'Action items' },
]

interface AppShellProps {
  children: ReactNode
  connectionStatus?: ConnectionStatus
}

export function AppShell({ children, connectionStatus }: AppShellProps) {
  const { theme, toggleTheme } = useTheme()
  const location = useLocation()

  return (
    <div className="min-h-screen bg-background">
      <header className="sticky top-0 z-50 h-14 w-full border-b border-border bg-background/80 backdrop-blur-sm">
        <div className="flex h-full max-w-screen-xl mx-auto items-center gap-3 px-6">
          <Link to="/dashboard" className="group flex items-center gap-2 shrink-0">
            <div className="flex h-5 w-5 items-center justify-center rounded border border-amber-500/40 bg-amber-500/10">
              <Flame className="h-3 w-3 text-amber-500" strokeWidth={2.25} />
            </div>
            <span className="font-mono text-[11px] tracking-[0.18em] uppercase text-muted-foreground group-hover:text-foreground select-none transition-colors">
              FLARE
            </span>
          </Link>

          <div className="h-4 w-px bg-border shrink-0" />

          <nav className="flex items-center gap-0.5">
            {NAV.map(item => {
              const active = location.pathname.startsWith(item.to)
              return (
                <Link
                  key={item.to}
                  to={item.to}
                  className={cn(
                    'rounded-sm px-2 py-1 text-xs font-medium transition-colors',
                    active ? 'text-foreground' : 'text-muted-foreground hover:text-foreground',
                  )}
                >
                  {item.label}
                </Link>
              )
            })}
          </nav>

          <div className="flex-1" />

          <div className="flex items-center gap-0.5">
            {connectionStatus !== undefined && (
              <ConnectionStatusDot status={connectionStatus} />
            )}

            <Button
              variant="ghost"
              size="icon"
              className="h-8 w-8 text-muted-foreground hover:text-foreground"
              onClick={toggleTheme}
              aria-label="Toggle theme"
            >
              {theme === 'dark' ? (
                <Sun className="h-3.5 w-3.5" />
              ) : (
                <Moon className="h-3.5 w-3.5" />
              )}
            </Button>

            <Button
              variant="ghost"
              size="sm"
              className="h-8 gap-1.5 px-2 font-mono text-[11px] text-muted-foreground hover:text-foreground"
              aria-label="Command palette"
              disabled
            >
              <Terminal className="h-3.5 w-3.5" />
              <span className="hidden sm:inline tracking-wide">⌘K</span>
            </Button>
          </div>
        </div>
      </header>

      <main className="max-w-screen-xl mx-auto px-6 py-8">{children}</main>
    </div>
  )
}
