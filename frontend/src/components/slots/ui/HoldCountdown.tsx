import { useEffect, useRef, useState } from 'react'
import { remainingMs, serverOffsetMs } from '@/utils/slots/serverClock'
import { formatCountdown } from '@/utils/slots/slotDates'

/**
 * Time left to pay, counted on the server's clock (ARCHITECTURE_CYCLE37.md §37.7.2): the offset between `serverTimeUtc` and the moment
 * the answer arrived is applied to the phone's clock. At zero the page refetches ONCE — the server decides what became of the hold.
 */
export function HoldCountdown({
  expiresAtUtc,
  serverTimeUtc,
  receivedAtMs,
  onExpire,
}: {
  expiresAtUtc: string
  serverTimeUtc: string
  /** `dataUpdatedAt` of the booking query: when the answer arrived on this phone. */
  receivedAtMs: number
  onExpire: () => void
}) {
  const offset = serverOffsetMs(serverTimeUtc, receivedAtMs)
  const [now, setNow] = useState(() => Date.now())
  const fired = useRef(false)

  useEffect(() => {
    fired.current = false
  }, [expiresAtUtc])

  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [])

  const left = remainingMs(expiresAtUtc, now, offset)
  useEffect(() => {
    if (left <= 0 && !fired.current) {
      fired.current = true
      onExpire()
    }
  }, [left, onExpire])

  const urgent = left < 5 * 60_000
  return (
    <p className="flex items-baseline gap-3" role="timer" aria-label="Осталось времени на оплату">
      <span className={`font-serif text-5xl tabular-nums ${urgent ? 'text-danger' : 'text-ink'}`}>{formatCountdown(left)}</span>
      <span className="text-sm text-ink-soft">минут:секунд</span>
    </p>
  )
}
