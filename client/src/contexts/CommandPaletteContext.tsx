import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'

interface CommandPaletteContextValue {
  open: boolean
  setOpen: (next: boolean) => void
  toggle: () => void
}

const CommandPaletteContext = createContext<CommandPaletteContextValue | null>(null)

interface CommandPaletteProviderProps {
  children: ReactNode
}

export function CommandPaletteProvider({ children }: CommandPaletteProviderProps) {
  const [open, setOpen] = useState(false)
  const toggle = useCallback(() => setOpen(prev => !prev), [])
  const value = useMemo(() => ({ open, setOpen, toggle }), [open, toggle])
  return (
    <CommandPaletteContext.Provider value={value}>
      {children}
    </CommandPaletteContext.Provider>
  )
}

export function useCommandPalette(): CommandPaletteContextValue {
  const ctx = useContext(CommandPaletteContext)
  if (ctx === null) {
    throw new Error('useCommandPalette must be used inside CommandPaletteProvider')
  }
  return ctx
}
