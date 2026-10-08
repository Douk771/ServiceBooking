import { useEffect, useState } from 'react'

const CHECK_INTERVAL_MS = 5 * 60 * 1000
const BUNDLE_RE = /\/assets\/index-[\w-]+\.js/

/** Path of the entry bundle this page was loaded with; null in dev (no hashed entry), where the check is skipped. */
export function loadedBundle(): string | null {
  const src = document.querySelector<HTMLScriptElement>('script[type="module"][src*="/assets/index-"]')?.src
  return src ? (BUNDLE_RE.exec(src)?.[0] ?? null) : null
}

/**
 * A Home Screen app on iOS is suspended, not reloaded, so it can keep an old build for days after a deploy.
 * Compares the entry bundle of the running page with the one in a fresh index.html (checked on return to the
 * app and every few minutes) and reports whether a newer build exists. Network errors are silent — the next
 * check retries.
 */
export function useAppUpdate(): boolean {
  const [updateAvailable, setUpdateAvailable] = useState(false)

  useEffect(() => {
    const current = loadedBundle()
    if (!current || updateAvailable) return

    let cancelled = false
    const check = async () => {
      try {
        const res = await fetch('/index.html', { cache: 'no-store' })
        if (!res.ok) return
        const latest = BUNDLE_RE.exec(await res.text())?.[0]
        if (!cancelled && latest && latest !== current) setUpdateAvailable(true)
      } catch {
        // offline or transient failure: try again on the next tick
      }
    }
    const onVisible = () => {
      if (document.visibilityState === 'visible') void check()
    }

    document.addEventListener('visibilitychange', onVisible)
    const timer = window.setInterval(onVisible, CHECK_INTERVAL_MS)
    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVisible)
      window.clearInterval(timer)
    }
  }, [updateAvailable])

  return updateAvailable
}
