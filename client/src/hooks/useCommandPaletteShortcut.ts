import { useEffect } from 'react'
import { useCommandPalette } from '@/contexts/CommandPaletteContext'

// Global ⌘K / Ctrl+K listener. Lives in its own file so Fast Refresh can
// hot-reload the palette component without invalidating the hook (and vice
// versa) — co-locating both in CommandPalette.tsx tripped
// `react-refresh/only-export-components`.
export function useCommandPaletteShortcut() {
  const { toggle } = useCommandPalette()
  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key !== 'k') return
      if (!event.metaKey && !event.ctrlKey) return
      event.preventDefault()
      toggle()
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [toggle])
}
