import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ConsentGate } from './ConsentGate'
import { LegalUpdateBanner } from './LegalUpdateBanner'
import { PlatformNoticeBanner } from './PlatformNoticeBanner'
import { legalApi } from '../../api/legal'
import { useAuthStore } from '../../store/authStore'
import { useLegalStore } from '../../store/legalStore'

interface Props {
  children: ReactNode
  /** Route prefixes that stay reachable while a "Material" change is pending acceptance — the caller
   *  (ezbook `App.tsx`, goods `GoodsApp.tsx`) owns its own list, since the route maps differ. */
  bypassPaths: readonly string[]
  /** Cycle 20 platform notices (US-20-03). ezbook only: the banner links to ezbook's `/notices`, which goods
   *  has no route for. */
  showPlatformNotices?: boolean
}

/**
 * Owns the single `legal-consent-status` query for the whole authenticated session (T-F2). Renders
 * ConsentGate full-screen on a "Material" change (unless the current route is one of the few still
 * reachable per US-39 п. 9), otherwise renders the app with LegalUpdateBanner for "Editorial" changes.
 *
 * Moved out of `App.tsx` unchanged in cycle 23 (ARCHITECTURE_CYCLE23.md §389.3-6) so both frontends
 * share it; only the bypass list became a prop.
 */
export function LegalGuard({ children, bypassPaths, showPlatformNotices = false }: Props) {
  const token = useAuthStore((s) => s.token)
  const consentRequiredFlag = useLegalStore((s) => s.consentRequired)
  const location = useLocation()

  const { data: status } = useQuery({
    queryKey: ['legal-consent-status'],
    queryFn: legalApi.getConsentStatus,
    enabled: !!token,
  })

  const requiresAcceptance = !!token && (consentRequiredFlag || status?.requiresAcceptance === true)
  const bypass = bypassPaths.some((p) => location.pathname.startsWith(p))

  if (requiresAcceptance && !bypass && status) {
    return <ConsentGate status={status} />
  }

  return (
    <>
      {status?.showBanner && !requiresAcceptance && <LegalUpdateBanner status={status} />}
      {/* US-20-03 (Т20-02) — next to LegalUpdateBanner, per ARCHITECTURE_CYCLE20.md §404.5. Only for
          an authenticated caller: `usePlatformNotices` (shared with NoticesPage/BillingNoticesSummary,
          both reachable only from already-authenticated routes) has no `enabled` guard of its own, so
          gating the mount here is what avoids a doomed 401 request on every anonymous page load.
          §404.5 explicitly does NOT build a bypass for this under a pending ConsentGate (the
          `requiresAcceptance` branch above returns early without reaching this component at all) — the
          backend route stays reachable via API regardless, this is a frontend simplification the
          architecture accepted. */}
      {showPlatformNotices && !!token && <PlatformNoticeBanner />}
      {children}
    </>
  )
}
