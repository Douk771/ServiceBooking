/** Minimum gap between two automatic reloads of the "Демо обновляется" screen (same period as the status poll). */
export const DEMO_RELOAD_MIN_GAP_MS = 15_000
const STORAGE_KEY = 'demo-auto-reload-at'

/**
 * How long to wait before the next automatic reload is allowed. If `GET /api/demo/status` says "reset is over" while
 * the API still answers 503 + `X-Demo-Resetting` (a race at the very end of a reset), the page would reload, hit the
 * 503, show the screen, read the same status, reload... at full speed. The timestamp of the last automatic reload lives
 * in `sessionStorage` (it survives the reload) and caps that loop at one reload per `DEMO_RELOAD_MIN_GAP_MS`.
 * Storage failures (private mode) mean "no limit", as before.
 */
export function demoReloadDelayMs(now: number, storage: Pick<Storage, 'getItem'> | null): number {
  let last = Number.NaN
  try {
    last = Number(storage?.getItem(STORAGE_KEY))
  } catch {
    return 0
  }
  if (!Number.isFinite(last) || last <= 0 || last > now) return 0
  return Math.max(0, DEMO_RELOAD_MIN_GAP_MS - (now - last))
}

/** Reloads the page, at most once per `DEMO_RELOAD_MIN_GAP_MS`; returns a cancel function for the pending reload. */
export function reloadDemoThrottled(reload: () => void = () => window.location.reload()): () => void {
  const storage = (() => {
    try {
      return window.sessionStorage
    } catch {
      return null
    }
  })()
  const timer = window.setTimeout(() => {
    try {
      storage?.setItem(STORAGE_KEY, String(Date.now()))
    } catch {
      /* ignore */
    }
    reload()
  }, demoReloadDelayMs(Date.now(), storage))
  return () => window.clearTimeout(timer)
}
