import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from '@testing-library/react'
import { useInitialHashScroll } from './useInitialHashScroll'

function Probe() {
  useInitialHashScroll()
  return <section id="biz-title">Для бизнеса</section>
}

describe('useInitialHashScroll', () => {
  const scrollIntoView = vi.fn()
  beforeEach(() => {
    scrollIntoView.mockClear()
    Element.prototype.scrollIntoView = scrollIntoView
  })
  afterEach(() => {
    window.location.hash = ''
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
    expect(scrollIntoView).not.toHaveBeenCalled()
  })
})
