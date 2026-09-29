import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ConsentGate } from './ConsentGate'
import { LegalUpdateBanner } from './LegalUpdateBanner'
import { legalApi } from '../../api/legal'
import { useAuthStore } from '../../store/authStore'
import { useLegalStore } from '../../store/legalStore'

interface Props {
  children: ReactNode
  /** Route prefixes that stay reachable while a "Material" change is pending acceptance — the caller
   *  (ezbook `App.tsx`, goods `GoodsApp.tsx`) owns its own list, since the route maps differ. */
  bypassPaths: readonly string[]
}

/**
 * Owns the single `legal-consent-status` query for the whole authenticated session (T-F2). Renders
 * ConsentGate full-screen on a "Material" change (unless the current route is one of the few still
 * reachable per US-39 п. 9), otherwise renders the app with LegalUpdateBanner for "Editorial" changes.
 *
 * Moved out of `App.tsx` unchanged in cycle 23 (ARCHITECTURE_CYCLE23.md §389.3-6) so both frontends
 * share it; only the bypass list became a prop.
 */
export function LegalGuard({ children, bypassPaths }: Props) {
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
      {children}
    </>
  )
}
