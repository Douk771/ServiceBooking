import { useEffect } from 'react'

/**
 * API_CONTRACT_CYCLE28.md §591 — `<meta name="robots" content="noindex, nofollow">` while a showcase company's page is
 * open, removed again when the visitor leaves (an SPA keeps the same document, so a stale tag would deindex the next page).
 * The nginx `X-Robots-Tag` for `primer-` slugs is the primary line; this is the second one.
 */
export function useNoindexMeta(active: boolean): void {
  useEffect(() => {
    if (!active) return
    const meta = document.createElement('meta')
    meta.name = 'robots'
    meta.content = 'noindex, nofollow'
    document.head.appendChild(meta)
    return () => {
      meta.remove()
    }
  }, [active])
}
