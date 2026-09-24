import { describe, it, expect, vi } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useCarousel } from './useCarousel'

function touch(x: number, y: number) {
  return { clientX: x, clientY: y } as unknown as React.Touch
}

describe('useCarousel — ARCHITECTURE_CYCLE13.md §211 (R11)', () => {
  it('starts at index 0', () => {
    const { result } = renderHook(() => useCarousel(3))
    expect(result.current.index).toBe(0)
  })

  it('next() wraps from the last slide back to the first', () => {
    const { result } = renderHook(() => useCarousel(3))
    act(() => result.current.next())
    act(() => result.current.next())
    expect(result.current.index).toBe(2)
    act(() => result.current.next())
    expect(result.current.index).toBe(0)
  })

  it('prev() wraps from the first slide to the last', () => {
    const { result } = renderHook(() => useCarousel(3))
    act(() => result.current.prev())
    expect(result.current.index).toBe(2)
  })

  it('ArrowRight/ArrowLeft on keydown move the index, other keys are ignored', () => {
    const { result } = renderHook(() => useCarousel(3))
    const preventDefault = vi.fn()
    act(() => result.current.onKeyDown({ key: 'ArrowRight', preventDefault } as unknown as React.KeyboardEvent))
    expect(result.current.index).toBe(1)
    expect(preventDefault).toHaveBeenCalled()
    act(() => result.current.onKeyDown({ key: 'Enter', preventDefault: vi.fn() } as unknown as React.KeyboardEvent))
    expect(result.current.index).toBe(1)
    act(() => result.current.onKeyDown({ key: 'ArrowLeft', preventDefault: vi.fn() } as unknown as React.KeyboardEvent))
    expect(result.current.index).toBe(0)
  })

  it('goTo() jumps directly, ignoring out-of-range indices', () => {
    const { result } = renderHook(() => useCarousel(5))
    act(() => result.current.goTo(3))
    expect(result.current.index).toBe(3)
    act(() => result.current.goTo(99))
    expect(result.current.index).toBe(3)
    act(() => result.current.goTo(-1))
    expect(result.current.index).toBe(3)
  })

  it('a horizontal swipe past the threshold advances/retreats a slide', () => {
    const { result } = renderHook(() => useCarousel(3))
    const { onTouchStart, onTouchEnd } = result.current.touchHandlers
    act(() => onTouchStart({ touches: [touch(200, 100)] } as unknown as React.TouchEvent))
    act(() => onTouchEnd({ changedTouches: [touch(100, 105)] } as unknown as React.TouchEvent))
    expect(result.current.index).toBe(1) // swiped left -> next

    act(() => onTouchStart({ touches: [touch(100, 100)] } as unknown as React.TouchEvent))
    act(() => onTouchEnd({ changedTouches: [touch(200, 100)] } as unknown as React.TouchEvent))
    expect(result.current.index).toBe(0) // swiped right -> prev
  })

  it('ignores a swipe below the |dx| > 40 threshold', () => {
    const { result } = renderHook(() => useCarousel(3))
    const { onTouchStart, onTouchEnd } = result.current.touchHandlers
    act(() => onTouchStart({ touches: [touch(100, 100)] } as unknown as React.TouchEvent))
    act(() => onTouchEnd({ changedTouches: [touch(130, 100)] } as unknown as React.TouchEvent))
    expect(result.current.index).toBe(0)
  })

  it('ignores a mostly-vertical drag (scroll intent, not swipe)', () => {
    const { result } = renderHook(() => useCarousel(3))
    const { onTouchStart, onTouchEnd } = result.current.touchHandlers
    act(() => onTouchStart({ touches: [touch(100, 100)] } as unknown as React.TouchEvent))
    act(() => onTouchEnd({ changedTouches: [touch(50, 300)] } as unknown as React.TouchEvent))
    expect(result.current.index).toBe(0)
  })

  it('never calls preventDefault on touchmove, leaving vertical scroll to the browser', () => {
    const { result } = renderHook(() => useCarousel(3))
    const preventDefault = vi.fn()
    act(() => result.current.touchHandlers.onTouchMove({ preventDefault } as unknown as React.TouchEvent))
    expect(preventDefault).not.toHaveBeenCalled()
  })

  it('has no autoplay: the index never changes on its own, even after 10s of fake timers (П9)', () => {
    vi.useFakeTimers()
    try {
      const { result } = renderHook(() => useCarousel(3))
      act(() => {
        vi.advanceTimersByTime(10_000)
      })
      expect(result.current.index).toBe(0)
    } finally {
      vi.useRealTimers()
    }
  })

  it('snaps back to 0 if the photo set shrinks below the current index', () => {
    const { result, rerender } = renderHook(({ length }) => useCarousel(length), { initialProps: { length: 3 } })
    act(() => result.current.goTo(2))
    expect(result.current.index).toBe(2)
    rerender({ length: 1 })
    expect(result.current.index).toBe(0)
  })
})
