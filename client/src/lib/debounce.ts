// Pure debouncer for testing. The React hook in hooks/useDebouncedCallback.ts
// wraps this so the timer behaviour can be exercised in vitest under
// `node` env without dragging in jsdom / @testing-library.
export interface Debouncer<TArgs extends unknown[]> {
  call: (...args: TArgs) => void
  // Cancel without invoking. Use on unmount when the pending value should
  // be dropped.
  cancel: () => void
  // Invoke a pending call immediately with whatever args were last
  // scheduled. Returns true if a flush happened, false if nothing was
  // pending. Use when a parent is about to close and the trailing edit
  // must reach the server.
  flush: () => boolean
}

export function createDebouncer<TArgs extends unknown[]>(
  fn: (...args: TArgs) => void,
  delayMs: number,
): Debouncer<TArgs> {
  let timer: ReturnType<typeof setTimeout> | null = null
  let pendingArgs: TArgs | null = null
  return {
    call(...args) {
      if (timer !== null) clearTimeout(timer)
      pendingArgs = args
      timer = setTimeout(() => {
        timer = null
        const toFire = pendingArgs
        pendingArgs = null
        if (toFire !== null) fn(...toFire)
      }, delayMs)
    },
    cancel() {
      if (timer !== null) clearTimeout(timer)
      timer = null
      pendingArgs = null
    },
    flush() {
      if (timer === null) return false
      clearTimeout(timer)
      timer = null
      const toFire = pendingArgs
      pendingArgs = null
      if (toFire !== null) fn(...toFire)
      return true
    },
  }
}
