import { useEffect, useState } from 'react'
import { useDemoStatus } from '../../hooks/useDemoStatus'
import { Icon } from '../ui/Icon'

/** API_CONTRACT_CYCLE28.md §600a — verbatim. */
export const DEMO_MAINTENANCE_TEXT = 'Демо обновляется, зайдите через минуту'

/**
 * Full-screen "the demo is being reset" message (not an error). Asks `GET /api/demo/status` right away and then every
 * 15 s; as soon as a fresh answer says the reset is over (or that this is not a demo any more) the page reloads, so
 * nothing half-loaded from before the reset stays on screen.
 */
export function DemoMaintenanceScreen({ onReload = () => window.location.reload() }: { onReload?: () => void }) {
  const { status, isFetching, refetch } = useDemoStatus({ poll: true })
  // The cached status may predate the 503 that brought us here ("not resetting" from page load), so nothing is trusted
  // until the request made on mount has come back.
  const [answered, setAnswered] = useState(false)

  useEffect(() => {
    let alive = true
    void refetch().then((r) => {
      if (alive && r.status === 'success') setAnswered(true)
    })
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const resetOver = answered && !isFetching && (status === null || status.resetting === false)

  useEffect(() => {
    if (resetOver) onReload()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetOver])

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="demo-maintenance"
      className="fixed inset-0 z-[100] flex items-center justify-center bg-cream px-6 font-sans"
    >
      <div className="max-w-[420px] text-center">
        <span className="mx-auto mb-7 flex h-14 w-14 items-center justify-center rounded-full bg-ink">
          <Icon name="clock" size={24} className="animate-pulse text-cream" strokeWidth={1.6} />
        </span>
        <h1 className="mb-3 font-serif text-[28px] font-medium text-ink">{DEMO_MAINTENANCE_TEXT}</h1>
        <p className="text-sm leading-relaxed text-ink-soft">Страница обновится сама, когда всё будет готово.</p>
      </div>
    </div>
  )
}
