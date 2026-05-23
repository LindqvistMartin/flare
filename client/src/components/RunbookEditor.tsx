import { useState } from 'react'
import MDEditor from '@uiw/react-md-editor'
import { useTheme } from '@/contexts/ThemeContext'
import { cn } from '@/lib/utils'

interface RunbookEditorProps {
  value: string
  onChange: (next: string) => void
  saving?: boolean
  // The component is uncontrolled-text-area-style: parent owns the latest
  // value, hook supplies the debounced save. `dirty` lets us subtitle the
  // editor with "unsaved" until the parent confirms a successful PUT.
  dirty?: boolean
}

// Markdown runbook editor with an Edit / Preview tab toggle. Uses
// @uiw/react-md-editor, which ships its own markdown renderer for the
// preview pane (not the react-markdown / remark-gfm stack used by
// RunbookSidebar). Preview is HTML-sanitised by MDEditor's default rehype
// pipeline — runbook authors can still write fenced code, tables, and
// headings, but raw <script> is stripped.
export function RunbookEditor({ value, onChange, saving, dirty }: RunbookEditorProps) {
  const { theme } = useTheme()
  const [mode, setMode] = useState<'edit' | 'preview'>('edit')

  return (
    <div data-color-mode={theme === 'dark' ? 'dark' : 'light'} className="space-y-2">
      <div className="flex items-center justify-between">
        <div role="tablist" aria-label="Runbook view" className="flex items-center gap-1">
          <ModeButton
            current={mode}
            value="edit"
            label="Edit"
            onSelect={setMode}
          />
          <ModeButton
            current={mode}
            value="preview"
            label="Preview"
            onSelect={setMode}
          />
        </div>
        <p
          aria-live="polite"
          className={cn(
            'font-mono text-[10px] tracking-wide uppercase',
            saving
              ? 'text-amber-600 dark:text-amber-400'
              : dirty
                ? 'text-muted-foreground'
                : 'text-emerald-600 dark:text-emerald-400',
          )}
        >
          {saving ? 'Saving…' : dirty ? 'Unsaved' : 'Saved'}
        </p>
      </div>

      <MDEditor
        value={value}
        onChange={next => onChange(next ?? '')}
        preview={mode === 'edit' ? 'edit' : 'preview'}
        height={360}
        textareaProps={{
          placeholder: '# Service runbook\n\nWhat to do when this service pages.',
        }}
        hideToolbar={false}
        visibleDragbar={false}
      />
    </div>
  )
}

interface ModeButtonProps {
  current: 'edit' | 'preview'
  value: 'edit' | 'preview'
  label: string
  onSelect: (next: 'edit' | 'preview') => void
}

function ModeButton({ current, value, label, onSelect }: ModeButtonProps) {
  const active = current === value
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={() => onSelect(value)}
      className={cn(
        'rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide transition-colors',
        active
          ? 'border-foreground/40 bg-foreground/5 text-foreground'
          : 'border-border bg-transparent text-muted-foreground hover:bg-muted/40 hover:text-foreground',
      )}
    >
      {label}
    </button>
  )
}
