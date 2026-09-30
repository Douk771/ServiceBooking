import type { ReactNode } from 'react'
import { useDemoStatus } from '../../hooks/useDemoStatus'
import { useNoindexMeta } from '../../hooks/useNoindexMeta'
import { useDemoStore } from '../../store/demoStore'
import { DemoMaintenanceScreen } from './DemoMaintenanceScreen'

/**
 * App-level switch for the demo stand (API_CONTRACT_CYCLE28.md §597, §600a):
 *  - `<meta name="robots" content="noindex, nofollow">` on every page while in demo (nginx + API headers are the
 *    primary line, this is the second one);
 *  - the whole app is replaced by `DemoMaintenanceScreen` while the stand is being reset — either the status says so
 *    (page opened mid-reset) or any API call came back 503 + `X-Demo-Resetting`.
 * Outside the demo (404 on the status) it is a transparent wrapper.
 */
export function DemoMaintenanceGate({ children }: { children: ReactNode }) {
  const { status, isDemo } = useDemoStatus()
  const apiSaysResetting = useDemoStore((s) => s.resetting)
  useNoindexMeta(isDemo)

  if (apiSaysResetting || (isDemo && status?.resetting === true)) return <DemoMaintenanceScreen />
  return <>{children}</>
}
