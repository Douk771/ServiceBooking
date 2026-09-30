import { describe, it, expect, vi, afterEach } from 'vitest'
import { demoReloadDelayMs, reloadDemoThrottled, DEMO_RELOAD_MIN_GAP_MS } from './demoReload'

const store = (v: string | null) => ({ getItem: () => v })

describe('demoReloadDelayMs', () => {
  it('no previous reload: no delay', () => {
    expect(demoReloadDelayMs(1_000_000, store(null))).toBe(0)
    expect(demoReloadDelayMs(1_000_000, null)).toBe(0)
  })
  it('previous reload just happened: wait for the rest of the gap', () => {
    expect(demoReloadDelayMs(1_005_000, store('1000000'))).toBe(DEMO_RELOAD_MIN_GAP_MS - 5_000)
  })
  it('gap already over, garbage or a timestamp from the future: no delay', () => {
    expect(demoReloadDelayMs(1_000_000 + DEMO_RELOAD_MIN_GAP_MS, store('1000000'))).toBe(0)
    expect(demoReloadDelayMs(1_000_000, store('abc'))).toBe(0)
    expect(demoReloadDelayMs(1_000_000, store('2000000'))).toBe(0)
  })
  it('storage that throws: no delay', () => {
    expect(
      demoReloadDelayMs(1, {
        getItem: () => {
          throw new Error('denied')
        },
      }),
    ).toBe(0)
  })
})

describe('reloadDemoThrottled', () => {
  afterEach(() => {
    vi.useRealTimers()
    window.sessionStorage.clear()
  })

  it('first reload is immediate and stamps the time; the next one is held back for the gap', () => {
    vi.useFakeTimers()
    vi.setSystemTime(1_000_000)
    const reload = vi.fn()
    reloadDemoThrottled(reload)
    vi.advanceTimersByTime(0)
    expect(reload).toHaveBeenCalledTimes(1)

    reloadDemoThrottled(reload)
    vi.advanceTimersByTime(DEMO_RELOAD_MIN_GAP_MS - 1)
    expect(reload).toHaveBeenCalledTimes(1)
    vi.advanceTimersByTime(1)
    expect(reload).toHaveBeenCalledTimes(2)
  })

  it('cancel drops the pending reload', () => {
    vi.useFakeTimers()
    const reload = vi.fn()
    const cancel = reloadDemoThrottled(reload)
    cancel()
    vi.advanceTimersByTime(DEMO_RELOAD_MIN_GAP_MS)
    expect(reload).not.toHaveBeenCalled()
  })
})
