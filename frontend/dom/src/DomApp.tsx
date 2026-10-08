import type { ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, Outlet, useLocation } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { queryClient } from '@/queryClient'
import { useAuthStore } from '@/store/authStore'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { LegalGuard } from '@/components/legal/LegalGuard'
import { OwnerTermsGateModal } from '@/components/legal/OwnerTermsGateModal'
import { LoginPage } from '@/pages/LoginPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { SubjectRequestPage } from '@/pages/SubjectRequestPage'
import { LegalDocumentPage } from '@/pages/LegalDocumentPage'
import { DomNavbar } from './components/DomNavbar'
import { DomFooter } from './components/DomFooter'
import { NotFoundPage } from './pages/NotFoundPage'
import { CatalogPage } from './pages/CatalogPage'
import { CompanyPage } from './pages/CompanyPage'
import { HousePage } from './pages/HousePage'
import { BookingPage } from './pages/BookingPage'
import { CabinetHomePage } from './pages/cabinet/CabinetHomePage'
import { CreateCompanyPage } from './pages/cabinet/CreateCompanyPage'
import { CompanyLayout, CompanyIndexRedirect } from './pages/cabinet/CompanyLayout'
import { SettingsPage } from './pages/cabinet/SettingsPage'
import { StaffPage } from './pages/cabinet/StaffPage'
import { NotificationsPage } from './pages/cabinet/NotificationsPage'
import { LinkPage } from './pages/cabinet/LinkPage'
import { SubscriptionPage } from './pages/cabinet/SubscriptionPage'
import { HousesPage } from './pages/cabinet/HousesPage'
import { HouseCreatePage } from './pages/cabinet/HouseCreatePage'
import { HouseEditPage } from './pages/cabinet/HouseEditPage'
import { BoardPage } from './pages/cabinet/BoardPage'
import { BookingsPage } from './pages/cabinet/BookingsPage'
import { BookingCardPage } from './pages/cabinet/BookingCardPage'
import { SchedulePage } from './pages/cabinet/SchedulePage'

/**
 * Routes reachable while a "Material" change to a GLOBAL-gate document is pending acceptance (same reasoning as ezbook's
 * list in App.tsx, API_CONTRACT_CYCLE5.md §41/§48.4): the legal pages themselves, the data request and «Мои согласия».
 * Every legal route of contracts/cycle11/legal-routes.json must be listed — checked by legalRoutes.test.ts.
 */
const CONSENT_GATE_BYPASS_PATHS = [
  '/privacy',
  '/terms',
  '/terms-owner',
  '/pdn-consent',
  '/channel-risk',
  '/offer-channel',
  '/payment-terms',
  '/data-request',
  '/profile/consents',
]

/** Signed-in only; otherwise to /login and back (`returnTo`), so a deep link into the cabinet survives sign-in. */
function RequireAuth() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated())
  const location = useLocation()
  if (!isAuthenticated)
    return <Navigate to={`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`} replace />
  return <Outlet />
}

/** Remounts on navigation so one crashed page does not stick after «К каталогу» (same pattern as ezbook, §103.1). */
function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation()
  return <ErrorBoundary key={location.pathname}>{children}</ErrorBoundary>
}

/**
 * dom.ezbook.ru (ARCHITECTURE_CYCLE37.md §37.14.5). The route table mirrors contracts/cycle37/dom-routes.json
 * (`spaRoutes` + `/:slug` + `/:slug/:houseSlug`); domRoutes.test.ts checks both directions, and every first segment must be a
 * reserved word so a company address can never shadow a route.
 */
export function DomApp() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <div className="min-h-screen bg-cream font-sans text-ink">
          <DomNavbar />
          <LegalGuard bypassPaths={CONSENT_GATE_BYPASS_PATHS}>
            <RouteErrorBoundary>
              <Routes>
                <Route path="/" element={<CatalogPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/register" element={<RegisterPage />} />
                <Route path="/b/:token" element={<BookingPage />} />

                <Route element={<RequireAuth />}>
                  <Route path="/cabinet" element={<CabinetHomePage />} />
                  <Route path="/cabinet/new" element={<CreateCompanyPage />} />
                  <Route path="/cabinet/subscription" element={<SubscriptionPage />} />
                  <Route element={<CompanyLayout />}>
                    <Route path="/cabinet/:companyId" element={<CompanyIndexRedirect />} />
                    <Route path="/cabinet/:companyId/board" element={<BoardPage />} />
                    <Route path="/cabinet/:companyId/bookings" element={<BookingsPage />} />
                    <Route path="/cabinet/:companyId/bookings/:bookingId" element={<BookingCardPage />} />
                    <Route path="/cabinet/:companyId/schedule" element={<SchedulePage />} />
                    <Route path="/cabinet/:companyId/houses" element={<HousesPage />} />
                    <Route path="/cabinet/:companyId/houses/new" element={<HouseCreatePage />} />
                    <Route path="/cabinet/:companyId/houses/:houseId" element={<HouseEditPage />} />
                    <Route path="/cabinet/:companyId/settings" element={<SettingsPage />} />
                    <Route path="/cabinet/:companyId/staff" element={<StaffPage />} />
                    <Route path="/cabinet/:companyId/notifications" element={<NotificationsPage />} />
                    <Route path="/cabinet/:companyId/link" element={<LinkPage />} />
                  </Route>
                </Route>

                <Route path="/data-request" element={<SubjectRequestPage />} />
                <Route path="/privacy" element={<LegalDocumentPage type="Privacy" />} />
                <Route path="/terms" element={<LegalDocumentPage type="TermsClient" />} />
                <Route path="/terms-owner" element={<LegalDocumentPage type="TermsOwner" />} />
                <Route path="/pdn-consent" element={<LegalDocumentPage type="PdnConsent" />} />
                <Route path="/channel-risk" element={<LegalDocumentPage type="ChannelRiskNotice" />} />
                <Route path="/offer-channel" element={<Navigate to="/terms-owner#offer-channel" replace />} />
                <Route path="/payment-terms" element={<Navigate to="/terms-owner#payment-terms" replace />} />

                {/* /:slug and /:slug/:houseSlug are the company and the house; static routes rank higher. */}
                <Route path="/:slug" element={<CompanyPage />} />
                <Route path="/:slug/:houseSlug" element={<HousePage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Routes>
            </RouteErrorBoundary>
            <DomFooter />
          </LegalGuard>
          <OwnerTermsGateModal />
        </div>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
