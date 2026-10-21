import { useEffect } from 'react'

/** How long the hash target stays pinned while the sections above it are still loading. */
export const HASH_PIN_MS = 8000

/** Any of these from the user ends the pin: from then on the page scrolls only where the user takes it. */
const USER_SCROLL_EVENTS = ['wheel', 'touchstart', 'keydown', 'pointerdown'] as const

function hashTarget(): HTMLElement | null {
  const raw = window.location.hash.slice(1)
  if (raw === '') return null
  let id = raw
  try {
    id = decodeURIComponent(raw)
  } catch {
    /* malformed escape: use the raw value */
  }
  return document.getElementById(id)
}

/**
 * Scrolls to the element named by `location.hash` once, after the landing mounts (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.7).
 * The browser tries the native anchor jump before React renders the page, so «<сайт>/#biz-title» from the products page would
 * otherwise land at the top. No element, empty hash or a malformed hash: nothing happens, never an error.
 *
 * Then the target is pinned: sections above it may still grow (the pricing section renders only after GET /api/pricing), and
 * Safari has no scroll anchoring, so «/#companies» — the last section of the business-first layout — would slide away by the
 * height of the pricing. While pinned, every resize of the page that moves the target re-scrolls to it. The pin also starts on
 * an in-page anchor click (`hashchange`), and ends on the first wheel/touch/key/pointer input or after HASH_PIN_MS.
 *
 * A target that is not rendered yet («/#companies» before the catalog knows it is not empty) is waited for the same way:
 * the first resize that brings it in scrolls to it and starts the pin. Input or HASH_PIN_MS ends the wait.
 */
export function useInitialHashScroll(): void {
  useEffect(() => {
    let stopPin: (() => void) | null = null

    const pin = (target: HTMLElement) => {
      stopPin?.()
      if (typeof ResizeObserver === 'undefined') return
      let lastTop = target.getBoundingClientRect().top
      const observer = new ResizeObserver(() => {
        if (!target.isConnected) return
        const top = target.getBoundingClientRect().top
        if (Math.abs(top - lastTop) < 1) return // scroll anchoring kept it in place, or the change was below it
        target.scrollIntoView({ block: 'start' })
        lastTop = target.getBoundingClientRect().top
      })
      observer.observe(document.body)
      const stop = () => {
        observer.disconnect()
        window.clearTimeout(timer)
        USER_SCROLL_EVENTS.forEach((e) => window.removeEventListener(e, stop, true))
        if (stopPin === stop) stopPin = null
      }
      const timer = window.setTimeout(stop, HASH_PIN_MS)
      USER_SCROLL_EVENTS.forEach((e) => window.addEventListener(e, stop, { capture: true, passive: true }))
      stopPin = stop
    }

    const initial = hashTarget()
    let stopWait: (() => void) | null = null
    if (initial) {
      initial.scrollIntoView({ block: 'start' })
      pin(initial)
    } else if (window.location.hash.length > 1 && typeof ResizeObserver !== 'undefined') {
      const observer = new ResizeObserver(() => {
        const target = hashTarget()
        if (!target) return
        stop()
        target.scrollIntoView({ block: 'start' })
        pin(target)
      })
      observer.observe(document.body)
      const stop = () => {
        observer.disconnect()
        window.clearTimeout(timer)
        USER_SCROLL_EVENTS.forEach((e) => window.removeEventListener(e, stop, true))
        stopWait = null
      }
      const timer = window.setTimeout(stop, HASH_PIN_MS)
      USER_SCROLL_EVENTS.forEach((e) => window.addEventListener(e, stop, { capture: true, passive: true }))
      stopWait = stop
    }

    // The browser has already jumped to the anchor; only keep it there.
    const onHashChange = () => {
      const target = hashTarget()
      if (target) pin(target)
    }
    window.addEventListener('hashchange', onHashChange)
    return () => {
      window.removeEventListener('hashchange', onHashChange)
      stopWait?.()
      stopPin?.()
    }
  }, [])
}
