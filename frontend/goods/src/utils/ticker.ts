/**
 * Calls `onTick` every `intervalMs`. Prefers a dedicated Web Worker (immune to background-tab timer throttling,
 * §397.1); falls back to a plain `setInterval` where workers are unavailable (old browsers, jsdom) — slower in a
 * hidden tab, but the `visibilitychange` poll still refreshes the moment the tab is shown again.
 */
export function createTicker(intervalMs: number, onTick: () => void): () => void {
  if (typeof Worker !== 'undefined') {
    try {
      let fallback: ReturnType<typeof setInterval> | undefined
      const worker = new Worker(new URL('../hooks/boardTimer.worker.ts', import.meta.url), { type: 'module' })
      worker.onmessage = () => onTick()
      worker.onerror = () => {
        // The worker failed to load/run: continue on the main thread instead of going silent.
        worker.terminate()
        fallback = setInterval(onTick, intervalMs)
      }
      worker.postMessage({ type: 'start', intervalMs })
      return () => {
        worker.postMessage({ type: 'stop' })
        worker.terminate()
        if (fallback !== undefined) clearInterval(fallback)
      }
    } catch {
      // fall through to the main-thread timer
    }
  }
  const id = setInterval(onTick, intervalMs)
  return () => clearInterval(id)
}
