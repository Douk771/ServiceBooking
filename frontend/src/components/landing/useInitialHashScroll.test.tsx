import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render } from '@testing-library/react'
import { HASH_PIN_MS, useInitialHashScroll } from './useInitialHashScroll'

function Probe() {
  useInitialHashScroll()
  return (
    <>
      <section id="biz-title">Для бизнеса</section>
      <section id="companies">Салоны на EZBOOK</section>
    </>
  )
}

/** jsdom has no layout and no ResizeObserver: the test drives both — the target's top and the «page resized» callback. */
class FakeResizeObserver {
  static instances: FakeResizeObserver[] = []
  disconnected = false
  constructor(private readonly cb: ResizeObserverCallback) {
    FakeResizeObserver.instances.push(this)
  }
  observe() {}
  unobserve() {}
  disconnect() {
    this.disconnected = true
  }
  fire() {
    if (!this.disconnected) this.cb([], this as unknown as ResizeObserver)
  }
}

const fireResize = () => act(() => FakeResizeObserver.instances.forEach((o) => o.fire()))

describe('useInitialHashScroll', () => {
  const scrollIntoView = vi.fn()
  let top = 0
  beforeEach(() => {
    scrollIntoView.mockReset()
    top = 0
    // scrollIntoView puts the target at the top of the viewport.
    scrollIntoView.mockImplementation(() => {
      top = 0
    })
    Element.prototype.scrollIntoView = scrollIntoView
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(() => ({ top }) as DOMRect)
    FakeResizeObserver.instances = []
    vi.stubGlobal('ResizeObserver', FakeResizeObserver)
  })
  afterEach(() => {
    window.location.hash = ''
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('scrolls once to the element named by the hash', () => {
    window.location.hash = '#biz-title'
    const { rerender } = render(<Probe />)
    rerender(<Probe />)
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'start' })
  })

  it('does nothing without a hash, for an unknown id and for a malformed hash', () => {
    render(<Probe />)
    window.location.hash = '#no-such-id'
    render(<Probe />)
    window.location.hash = '#%E0%A4%A'
    render(<Probe />)
    fireResize()
    expect(scrollIntoView).not.toHaveBeenCalled()
  })

  it('scrolls to the target again when a section above it loads later (pricing above #companies)', () => {
    window.location.hash = '#companies'
    render(<Probe />)
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    top = 420 // pricing rendered above the catalog and pushed it down
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(2)
    expect(top).toBe(0)
  })

  it('does not scroll again when the target did not move (scroll anchoring, a change below, pricing 404)', () => {
    window.location.hash = '#companies'
    render(<Probe />)
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('lets go after the user scrolls, after the pin time and on unmount', () => {
    window.location.hash = '#companies'
    render(<Probe />)
    act(() => {
      window.dispatchEvent(new Event('wheel'))
    })
    top = 420
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(1)

    vi.useFakeTimers()
    scrollIntoView.mockClear()
    const second = render(<Probe />)
    act(() => {
      vi.advanceTimersByTime(HASH_PIN_MS)
    })
    top = 420
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(1) // only the mount scroll

    scrollIntoView.mockClear()
    second.unmount()
    const third = render(<Probe />)
    third.unmount()
    top = 420
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('keeps an in-page anchor target in place after a hash change (hero link to the catalog)', () => {
    render(<Probe />)
    expect(scrollIntoView).not.toHaveBeenCalled()
    act(() => {
      window.location.hash = '#companies'
      window.dispatchEvent(new HashChangeEvent('hashchange'))
    })
    expect(scrollIntoView).not.toHaveBeenCalled() // the browser has already jumped
    top = 380
    fireResize()
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('works without ResizeObserver: the single mount scroll only', () => {
    vi.stubGlobal('ResizeObserver', undefined)
    window.location.hash = '#companies'
    render(<Probe />)
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })
})
