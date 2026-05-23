import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createDebouncer } from './debounce'

describe('createDebouncer', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('invokes the callback after the delay elapses', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    vi.advanceTimersByTime(499)
    expect(fn).not.toHaveBeenCalled()
    vi.advanceTimersByTime(1)
    expect(fn).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('collapses bursts into a single trailing call', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    vi.advanceTimersByTime(200)
    d.call('b')
    vi.advanceTimersByTime(200)
    d.call('c')
    vi.advanceTimersByTime(500)
    expect(fn).toHaveBeenCalledExactlyOnceWith('c')
  })

  it('cancel prevents a pending invocation', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    d.cancel()
    vi.advanceTimersByTime(1000)
    expect(fn).not.toHaveBeenCalled()
  })

  it('allows a new call after a completed flush', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    vi.advanceTimersByTime(500)
    d.call('b')
    vi.advanceTimersByTime(500)
    expect(fn).toHaveBeenNthCalledWith(1, 'a')
    expect(fn).toHaveBeenNthCalledWith(2, 'b')
  })

  it('flush invokes the pending call immediately and reports true', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    expect(d.flush()).toBe(true)
    expect(fn).toHaveBeenCalledExactlyOnceWith('a')
    vi.advanceTimersByTime(1000)
    expect(fn).toHaveBeenCalledOnce()
  })

  it('flush returns false when nothing is pending', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    expect(d.flush()).toBe(false)
    expect(fn).not.toHaveBeenCalled()
  })

  it('flush uses the most recent args after a burst', () => {
    const fn = vi.fn()
    const d = createDebouncer(fn, 500)
    d.call('a')
    d.call('b')
    d.call('c')
    d.flush()
    expect(fn).toHaveBeenCalledExactlyOnceWith('c')
  })
})
