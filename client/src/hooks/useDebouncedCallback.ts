import { useEffect, useMemo, useRef } from 'react'
import { createDebouncer } from '@/lib/debounce'

// Debounced wrapper around a callback. The callback identity can change every
// render — the hook reads the latest from a ref so the user does not have to
// memoise it. Cancels any pending invocation on unmount.
export function useDebouncedCallback<TArgs extends unknown[]>(
  callback: (...args: TArgs) => void,
  delayMs: number,
): (...args: TArgs) => void {
  const callbackRef = useRef(callback)
  useEffect(() => {
    callbackRef.current = callback
  }, [callback])

  // The ref is only read inside the setTimeout body — never during render —
  // but ESLint's static analyser cannot prove that, so we silence the
  // overly-strict refs rule for this single line. Closing over the ref is
  // the documented React pattern for "always invoke the latest callback"
  // without forcing the consumer to useCallback.
  const debouncer = useMemo(
    () =>
      // eslint-disable-next-line react-hooks/refs
      createDebouncer<TArgs>((...args) => callbackRef.current(...args), delayMs),
    [delayMs],
  )

  useEffect(() => debouncer.cancel, [debouncer])

  return debouncer.call
}
