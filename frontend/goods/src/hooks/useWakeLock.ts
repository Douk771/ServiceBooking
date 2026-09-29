import { useEffect, useState } from 'react'

export type WakeLockState = 'active' | 'inactive' | 'unsupported'

/**
 * Screen Wake Lock for the orders screen (§397.1): keeps a tablet from dimming. The lock is dropped by the
 * browser whenever the tab is hidden, so it is requested again on `visibilitychange`. Where the API does not
 * exist (Firefox, Safari < 16.4) the state says so and the screen shows a «отключите автоблокировку» hint.
 */
export function useWakeLock(): WakeLockState {
  const supported = typeof navigator !== 'undefined' && 'wakeLock' in navigator
  const [state, setState] = useState<WakeLockState>(supported ? 'inactive' : 'unsupported')

  useEffect(() => {
    if (!supported) return
    let sentinel: WakeLockSentinel | null = null
    let cancelled = false

    const acquire = async () => {
      if (document.visibilityState !== 'visible') return
      try {
        const s = await navigator.wakeLock.request('screen')
        if (cancelled) {
          void s.release()
          return
        }
        sentinel = s
        setState('active')
        s.addEventListener('release', () => {
          if (!cancelled) setState('inactive')
        })
      } catch {
        if (!cancelled) setState('inactive') // denied (battery saver, permissions policy)
      }
    }

    void acquire()
    const onVisible = () => {
      if (document.visibilityState === 'visible' && (!sentinel || sentinel.released)) void acquire()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVisible)
      void sentinel?.release().catch(() => {})
    }
  }, [supported])

  return state
}
