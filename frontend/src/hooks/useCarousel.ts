import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type TouchEvent } from 'react'

/**
 * ARCHITECTURE_CYCLE13.md §211/§204 (R11) — all carousel mechanics in one place, so the next carousel
 * in the product doesn't become a second, slightly-different implementation.
 *
 * Deliberately does NOT include a `setInterval`/`setTimeout` slide-advance of any kind — no
 * autoplay, ever (П9). `useCarousel.test.ts` asserts this with fake timers.
 */
export interface UseCarouselResult {
  /** Currently active slide, always `0 <= index < length` (except when `length === 0`). */
  index: number
  next: () => void
  prev: () => void
  goTo: (i: number) => void
  /** Attach to the carousel root's `onKeyDown` — ArrowLeft/ArrowRight only, wraps at the ends. */
  onKeyDown: (e: KeyboardEvent) => void
  /** Attach to the slide track. Horizontal-intent swipe only: `preventDefault` is never called on
   *  `touchmove`, so vertical page scroll (`touch-action: pan-y`) stays the browser's job. */
  touchHandlers: {
    onTouchStart: (e: TouchEvent) => void
    onTouchMove: (e: TouchEvent) => void
    onTouchEnd: (e: TouchEvent) => void
  }
}

const SWIPE_MIN_DX = 40
const SWIPE_DIRECTION_RATIO = 1.5

export function useCarousel(length: number): UseCarouselResult {
  const [index, setIndex] = useState(0)

  // If the set of photos shrinks (or starts empty) while a later slide was active, snap back to a
  // valid index instead of pointing past the end of the array.
  useEffect(() => {
    if (length > 0 && index >= length) setIndex(0)
  }, [length, index])

  const next = useCallback(() => {
    setIndex((v) => (length > 0 ? (v + 1) % length : 0))
  }, [length])

  const prev = useCallback(() => {
    setIndex((v) => (length > 0 ? (v - 1 + length) % length : 0))
  }, [length])

  const goTo = useCallback(
    (i: number) => {
      if (i >= 0 && i < length) setIndex(i)
    },
    [length],
  )

  const onKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') {
        e.preventDefault()
        next()
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault()
        prev()
      }
    },
    [next, prev],
  )

  const touchStart = useRef<{ x: number; y: number } | null>(null)

  const onTouchStart = useCallback((e: TouchEvent) => {
    const t = e.touches[0]
    touchStart.current = t ? { x: t.clientX, y: t.clientY } : null
  }, [])

  // Never calls preventDefault — `touch-action: pan-y` on the track leaves vertical page scroll to
  // the browser; we only read the gesture, we don't intercept it (§211).
  const onTouchMove = useCallback((_e: TouchEvent) => {}, [])

  const onTouchEnd = useCallback(
    (e: TouchEvent) => {
      const start = touchStart.current
      touchStart.current = null
      if (!start) return
      const t = e.changedTouches[0]
      if (!t) return
      const dx = t.clientX - start.x
      const dy = t.clientY - start.y
      if (Math.abs(dx) > SWIPE_MIN_DX && Math.abs(dx) > Math.abs(dy) * SWIPE_DIRECTION_RATIO) {
        if (dx < 0) next()
        else prev()
      }
    },
    [next, prev],
  )

  return { index, next, prev, goTo, onKeyDown, touchHandlers: { onTouchStart, onTouchMove, onTouchEnd } }
}
