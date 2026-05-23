// Pure debouncer for testing. The React hook in hooks/useDebouncedCallback.ts
// wraps this so the timer behaviour can be exercised in vitest under
// `node` env without dragging in jsdom / @testing-library.
export interface Debouncer<TArgs extends unknown[]> {
  call: (...args: TArgs) => void
  cancel: () => void
}

export function createDebouncer<TArgs extends unknown[]>(
  fn: (...args: TArgs) => void,
  delayMs: number,
): Debouncer<TArgs> {
  let timer: ReturnType<typeof setTimeout> | null = null
  return {
    call(...args) {
      if (timer !== null) clearTimeout(timer)
      timer = setTimeout(() => {
        timer = null
        fn(...args)
      }, delayMs)
    },
    cancel() {
      if (timer !== null) clearTimeout(timer)
      timer = null
    },
  }
}
