import { useEffect } from 'react'

/**
 * Scrolls to the element named by `location.hash` once, after the landing mounts (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.7).
 * The browser tries the native anchor jump before React renders the page, so «<сайт>/#biz-title» from the products page would
 * otherwise land at the top. No element, empty hash or a malformed hash: nothing happens, never an error.
 */
export function useInitialHashScroll(): void {
  useEffect(() => {
    const raw = window.location.hash.slice(1)
    if (raw === '') return
    let id = raw
    try {
      id = decodeURIComponent(raw)
    } catch {
      /* malformed escape: use the raw value */
    }
    document.getElementById(id)?.scrollIntoView({ block: 'start' })
  }, [])
}
