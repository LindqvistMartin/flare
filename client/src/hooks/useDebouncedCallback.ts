/* eslint-disable react-hooks/refs --
 * Standard React pattern: ref holds the latest callback so debounced timer
 * always invokes the current closure without forcing consumers to memoise.
 * The ref is only dereferenced inside setTimeout (createDebouncer.call),
 * never during render. The newer react-hooks/refs rule cannot prove that
 * statically and flags the whole closure expression.
 */
import { useEffect, useRef, useState } from 'react'
import { createDebouncer, type Debouncer } from '@/lib/debounce'

export type DebouncedHandle<TArgs extends unknown[]> = Debouncer<TArgs>

// Debounced wrapper around a callback. The callback identity can change every
// render — the hook reads the latest from a ref so the user does not have to
// memoise it. Cancels any pending invocation on unmount.
//
// Returns a handle exposing call / flush / cancel — callers that need to
// commit the trailing edit before unmount (dialog closing with unsaved input)
// call `flush()` themselves; default consumers can keep using `.call(...)`.
//
// `delayMs` is captured once on mount: the debouncer instance lives in
// useState lazy-init so React cannot evict it the way useMemo can. Changing
// the delay after mount is intentionally a no-op — none of today's callers
// vary the delay.
export function useDebouncedCallback<TArgs extends unknown[]>(
  callback: (...args: TArgs) => void,
  delayMs: number,
): DebouncedHandle<TArgs> {
  const callbackRef = useRef(callback)
  useEffect(() => {
    callbackRef.current = callback
  }, [callback])

  const [debouncer] = useState<Debouncer<TArgs>>(() =>
    createDebouncer<TArgs>((...args) => callbackRef.current(...args), delayMs),
  )

  useEffect(() => () => debouncer.cancel(), [debouncer])

  return debouncer
}
