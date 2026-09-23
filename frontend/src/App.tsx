import type { ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { queryClient } from './queryClient'
import { Navbar } from './components/layout/Navbar'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { RegisterPage } from './pages/RegisterPage'
import { CompanyPage } from './pages/CompanyPage'
import { MyBookingsPage } from './pages/MyBookingsPage'
import { ClientBookingsPage } from './pages/ClientBookingsPage'
import { EmbedPage } from './pages/EmbedPage'
import { CabinetPage } from './pages/CabinetPage'
import { CompanyManagePage } from './pages/owner/CompanyManagePage'
import { ProfilePage } from './pages/ProfilePage'
import { ConsentsPage } from './pages/ConsentsPage'
import { SubjectRequestPage } from './pages/SubjectRequestPage'
import { AdminPage } from './pages/AdminPage'
import { LegalDocumentPage } from './pages/LegalDocumentPage'
import { PricingPage } from './pages/PricingPage'
import { BillingPage } from './pages/BillingPage'
import { UnsubscribePage } from './pages/UnsubscribePage'
import { DeleteAccountPage } from './pages/DeleteAccountPage'
import { Footer } from './components/layout/Footer'
import { ConsentGate } from './components/legal/ConsentGate'
import { LegalUpdateBanner } from './components/legal/LegalUpdateBanner'
import { OwnerTermsGateModal } from './components/legal/OwnerTermsGateModal'
import { legalApi } from './api/legal'
import { useAuthStore } from './store/authStore'
import { useLegalStore } from './store/legalStore'

function ProtectedRoute({ children, roles }: { children: React.ReactNode; roles?: string[] }) {
  const { isAuthenticated, hasRole } = useAuthStore()
  if (!isAuthenticated()) return <Navigate to="/login" replace />
  if (roles && !roles.some(hasRole)) return <Navigate to="/" replace />
  return <>{children}</>
}

// Routes reachable while a "Material" change to a GLOBAL-gate document is pending acceptance
// (API_CONTRACT_CYCLE5.md §41, §48.4) — the same set the backend allow-lists, minus the auth/health
// endpoints that have no frontend page of their own. Owner-scope (`TermsOwner`) documents don't need
// an entry here: that gate never covers the screen (§42.2), so `OwnerTermsGateModal` handles it
// separately from anywhere. `/pricing` is also included: the public pricing showcase (T-cycle07)
// must stay visible to owners even mid-gate, since it's what tells them what they're accepting.
const CONSENT_GATE_BYPASS_PATHS = [
  '/privacy',
  '/terms',
  '/terms-owner',
  '/pdn-consent',
  '/channel-risk',
  '/offer-channel',
  '/payment-terms',
  '/data-request',
  '/profile/delete',
  '/profile/consents',
  '/u/',
  '/pricing',
]

/**
 * Owns the single `legal-consent-status` query for the whole authenticated session (T-F2). Renders
 * ConsentGate full-screen on a "Material" change (unless the current route is one of the few still
 * reachable per US-39 п. 9), otherwise renders the app with LegalUpdateBanner for "Editorial" changes.
 */
function LegalGuard({ children }: { children: ReactNode }) {
  const token = useAuthStore((s) => s.token)
  const consentRequiredFlag = useLegalStore((s) => s.consentRequired)
  const location = useLocation()

  const { data: status } = useQuery({
    queryKey: ['legal-consent-status'],
    queryFn: legalApi.getConsentStatus,
    enabled: !!token,
  })

  const requiresAcceptance = !!token && (consentRequiredFlag || status?.requiresAcceptance === true)
  const bypass = CONSENT_GATE_BYPASS_PATHS.some((p) => location.pathname.startsWith(p))

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

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          {/* Embed route — no Navbar, no consent gate: the widget is used by anonymous guests */}
          <Route
            path="/embed/:slug"
            element={
              <div className="min-h-screen bg-white font-sans">
                <EmbedPage />
              </div>
            }
          />

          {/* All other routes with Navbar */}
          <Route
            path="*"
            element={
              <div className="min-h-screen bg-cream font-sans text-ink">
                <Navbar />
                <LegalGuard>
                  <Routes>
                    <Route path="/" element={<HomePage />} />
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/register" element={<RegisterPage />} />
                    <Route path="/company/:slug" element={<CompanyPage />} />
                    <Route
                      path="/my-bookings"
                      element={
                        <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']}>
                          <MyBookingsPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/my-visits"
                      element={
                        <ProtectedRoute roles={['Client', 'Master', 'CompanyOwner', 'SuperAdmin']}>
                          <ClientBookingsPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/cabinet"
                      element={
                        <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']}>
                          <CabinetPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/owner/company/:id"
                      element={
                        <ProtectedRoute roles={['CompanyOwner', 'SuperAdmin']}>
                          <CompanyManagePage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/profile"
                      element={
                        <ProtectedRoute>
                          <ProfilePage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/profile/consents"
                      element={
                        <ProtectedRoute>
                          <ConsentsPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/profile/delete"
                      element={
                        <ProtectedRoute>
                          <DeleteAccountPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route
                      path="/admin"
                      element={
                        <ProtectedRoute roles={['SuperAdmin']}>
                          <AdminPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route path="/u/:token" element={<UnsubscribePage />} />
                    <Route path="/pricing" element={<PricingPage />} />
                    <Route
                      path="/billing"
                      element={
                        <ProtectedRoute roles={['CompanyOwner', 'SuperAdmin']}>
                          <BillingPage />
                        </ProtectedRoute>
                      }
                    />
                    <Route path="/data-request" element={<SubjectRequestPage />} />
                    <Route path="/privacy" element={<LegalDocumentPage type="Privacy" />} />
                    <Route path="/terms" element={<LegalDocumentPage type="TermsClient" />} />
                    <Route path="/terms-owner" element={<LegalDocumentPage type="TermsOwner" />} />
                    <Route path="/pdn-consent" element={<LegalDocumentPage type="PdnConsent" />} />
                    <Route path="/channel-risk" element={<LegalDocumentPage type="ChannelRiskNotice" />} />
                    {/* §43 — the channel offer (D9) is an appendix inside TermsOwner, not a document
                        of its own; this is a redirect, not a page. */}
                    <Route path="/offer-channel" element={<Navigate to="/terms-owner#offer-channel" replace />} />
                    {/* Приложение № 2 (оплата, автопродление, возврат) — тоже приложение внутри
                        TermsOwner, не отдельный документ; редирект по образцу /offer-channel. */}
                    <Route path="/payment-terms" element={<Navigate to="/terms-owner#payment-terms" replace />} />
                    {/* Legacy redirects */}
                    <Route path="/dashboard" element={<Navigate to="/cabinet" replace />} />
                    <Route path="/owner" element={<Navigate to="/cabinet" replace />} />
                  </Routes>
                  <Footer />
                </LegalGuard>
                <OwnerTermsGateModal />
              </div>
            }
          />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
