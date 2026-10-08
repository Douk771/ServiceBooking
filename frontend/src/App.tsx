import type { ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
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
import { CreateCompanyPage } from './pages/CreateCompanyPage'
import { CompanyManagePage } from './pages/owner/CompanyManagePage'
import { ProfilePage } from './pages/ProfilePage'
import { ConsentsPage } from './pages/ConsentsPage'
import { SubjectRequestPage } from './pages/SubjectRequestPage'
import { AdminPage } from './pages/AdminPage'
import { LegalDocumentPage } from './pages/LegalDocumentPage'
import { PricingPage } from './pages/PricingPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { BillingPage } from './pages/BillingPage'
import { UnsubscribePage } from './pages/UnsubscribePage'
import { DeleteAccountPage } from './pages/DeleteAccountPage'
import { HealthConsentFormPrintPage } from './pages/HealthConsentFormPrintPage'
import { Footer } from './components/layout/Footer'
import { ErrorBoundary } from './components/ErrorBoundary'
import { LegalGuard } from './components/legal/LegalGuard'
import { NoticesPage } from './pages/NoticesPage'
import { OwnerTermsGateModal } from './components/legal/OwnerTermsGateModal'
import { ProtectedRoute } from './components/auth/ProtectedRoute'
import { DemoBanner } from './components/demo/DemoBanner'
import { DemoMaintenanceGate } from './components/demo/DemoMaintenanceGate'

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
 * Б3 (code review): `<ErrorBoundary>` alone doesn't reset once it has caught — its `state.error`
 * survives a `<Link>` navigation, so a user who crashes one page and clicks "Главная" keeps seeing
 * the same cached error card until a full reload. Keying by pathname forces React to unmount and
 * remount the boundary (and its children) on every route change, mirroring the `key={tab}` pattern
 * already used in AdminPage/CabinetPage.
 */
function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation()
  return <ErrorBoundary key={location.pathname}>{children}</ErrorBoundary>
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/* Cycle 28 (§597, §600a): noindex + the "Демо обновляется" screen on the demo stand; transparent elsewhere. */}
        <DemoMaintenanceGate>
          <Routes>
            {/* Embed route — no Navbar, no consent gate: the widget is used by anonymous guests */}
            <Route
              path="/embed/:slug"
              element={
                <div className="min-h-screen bg-white font-sans">
                  <DemoBanner compact />
                  <EmbedPage />
                </div>
              }
            />

            {/* All other routes with Navbar */}
            <Route
              path="*"
              element={
                <div className="min-h-screen bg-cream font-sans text-ink">
                  <DemoBanner />
                  <Navbar />
                  <LegalGuard bypassPaths={CONSENT_GATE_BYPASS_PATHS} showPlatformNotices>
                    {/* Wraps only the page routes, not Navbar — a page-level render crash (e.g. an
                      unprotected field on a stale API response, §100.2) must not take the shell
                      down with it (§103.1). Keyed by pathname (RouteErrorBoundary below) so it
                      remounts on navigation — clicking back to "Главная" after a crash actually
                      recovers instead of showing the same cached error until a full reload,
                      matching the AdminPage/CabinetPage `key={tab}` pattern. */}
                    <RouteErrorBoundary>
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
                          path="/cabinet/new"
                          element={
                            <ProtectedRoute returnToPath="/cabinet/new">
                              <CreateCompanyPage />
                            </ProtectedRoute>
                          }
                        />
                        <Route
                          path="/cabinet"
                          element={
                            <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']} returnToPath="/cabinet" deniedTo="/cabinet/new">
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
                        {/* API_CONTRACT_CYCLE20.md §432 — SuperAdmin is 403 on every health-consent
                          route (§48.2 cycle 5 not weakened by this cycle); `SuperAdmin` deliberately
                          excluded from `roles` here, same as `HealthNoteCard` not even rendering for
                          that role in `MasterClientsPage`. */}
                        <Route
                          path="/companies/:companyId/clients/:clientKey/health-consent-form"
                          element={
                            <ProtectedRoute roles={['Master', 'CompanyOwner']}>
                              <HealthConsentFormPrintPage />
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
                        <Route
                          path="/notices"
                          element={
                            <ProtectedRoute>
                              <NoticesPage />
                            </ProtectedRoute>
                          }
                        />
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
                        {/* US-28-08 — an unknown address gets a real 404 page instead of an empty gap. */}
                        <Route path="*" element={<NotFoundPage />} />
                      </Routes>
                    </RouteErrorBoundary>
                    <Footer />
                  </LegalGuard>
                  <OwnerTermsGateModal />
                </div>
              }
            />
          </Routes>
        </DemoMaintenanceGate>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
