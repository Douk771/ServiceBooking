// Dedicated worker whose only job is to tick (ARCHITECTURE_CYCLE23.md §397.1): timers inside workers are not
// subject to the aggressive background-tab throttling that Chrome applies to main-thread timers, so a screen
// left on a tablet in another tab keeps polling and can still ring for a new order.
const ctx = self as unknown as {
  onmessage: ((e: MessageEvent<{ type: 'start'; intervalMs: number } | { type: 'stop' }>) => void) | null
  postMessage: (message: unknown) => void
}

let timer: ReturnType<typeof setInterval> | undefined

ctx.onmessage = (e) => {
  if (timer !== undefined) {
    clearInterval(timer)
    timer = undefined
  }
  if (e.data.type === 'start') {
    const ms = e.data.intervalMs
    timer = setInterval(() => ctx.postMessage('tick'), ms)
  }
}

export {}
