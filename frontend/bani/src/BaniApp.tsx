import type { ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, Outlet, useLocation } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { queryClient } from '@/queryClient'
import { useAuthStore } from '@/store/authStore'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { LegalGuard } from '@/components/legal/LegalGuard'
import { OwnerTermsGateModal } from '@/components/legal/OwnerTermsGateModal'
import { UpdateBanner } from '@/components/sites/UpdateBanner'
import { LoginPage } from '@/pages/LoginPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { SubjectRequestPage } from '@/pages/SubjectRequestPage'
import { LegalDocumentPage } from '@/pages/LegalDocumentPage'
import { ConsentsPage } from '@/pages/ConsentsPage'
import { NoticesPage } from '@/pages/NoticesPage'
import { BaniNavbar } from './components/BaniNavbar'
import { BaniFooter } from './components/BaniFooter'
import { CatalogPage } from './pages/CatalogPage'
import { CompanyPage } from './pages/CompanyPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ProfilePage } from './pages/ProfilePage'
import { ResourcePage } from './pages/ResourcePage'
import { OrderPage } from './pages/OrderPage'
import { MyBookingsPage } from './pages/MyBookingsPage'
import { CabinetSlotScope } from './cabinet/CabinetSlotScope'
import { CompanyLayout, CompanyIndexRedirect } from './pages/cabinet/CompanyLayout'
import { DayPage } from './pages/cabinet/DayPage'
import { OrdersPage } from './pages/cabinet/OrdersPage'
import { SessionPage } from './pages/cabinet/SessionPage'
import { ResourcesPage } from './pages/cabinet/ResourcesPage'
import { ResourceCreatePage } from './pages/cabinet/ResourceCreatePage'
import { ResourceEditPage } from './pages/cabinet/ResourceEditPage'
import { SchedulePage } from './pages/cabinet/SchedulePage'
import { CabinetHomePage } from './pages/cabinet/CabinetHomePage'
import { CreateCompanyPage } from './pages/cabinet/CreateCompanyPage'
import { SubscriptionPage } from './pages/cabinet/SubscriptionPage'
import { SettingsPage } from './pages/cabinet/SettingsPage'
import { StaffPage } from './pages/cabinet/StaffPage'
import { NotificationsPage } from './pages/cabinet/NotificationsPage'
import { LinkPage } from './pages/cabinet/LinkPage'

/**
 * Routes reachable while a "Material" change to a GLOBAL-gate document is pending acceptance (same reasoning as dom's list):
 * the legal pages themselves, the data request and «Мои согласия». Every legal route of contracts/cycle11/legal-routes.json
 * must be listed — checked by baniRoutes.test.ts.
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

/** Remounts on navigation so one crashed page does not stick after «На главную». */
function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation()
  return <ErrorBoundary key={location.pathname}>{children}</ErrorBoundary>
}

/**
 * bani.ezbook.ru (ARCHITECTURE_CYCLE42.md §42.12.2). The route table mirrors contracts/cycle42/bani-routes.json
 * (`spaRoutes` + `/:slug` + `/:slug/:resourceSlug`); baniRoutes.test.ts checks both directions, and every first segment
 * must be a reserved word so a company address can never shadow a route.
 */
export function BaniApp() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <div className="min-h-screen bg-cream font-sans text-ink">
          <BaniNavbar />
          <LegalGuard bypassPaths={CONSENT_GATE_BYPASS_PATHS}>
            <RouteErrorBoundary>
              <Routes>
                <Route path="/" element={<CatalogPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/register" element={<RegisterPage />} />
                <Route path="/s/:token" element={<OrderPage />} />

                <Route element={<RequireAuth />}>
                  <Route path="/bookings" element={<MyBookingsPage />} />
                  <Route path="/profile" element={<ProfilePage />} />
                  <Route path="/profile/consents" element={<ConsentsPage />} />
                  <Route path="/notices" element={<NoticesPage />} />
                  <Route path="/cabinet" element={<CabinetHomePage />} />
                  <Route path="/cabinet/new" element={<CreateCompanyPage />} />
                  <Route path="/cabinet/subscription" element={<SubscriptionPage />} />
                  <Route element={<CompanyLayout />}>
                    <Route path="/cabinet/:companyId" element={<CompanyIndexRedirect />} />
                    <Route element={<CabinetSlotScope />}>
                      <Route path="/cabinet/:companyId/service-day/:date" element={<DayPage />} />
                      <Route path="/cabinet/:companyId/orders" element={<OrdersPage />} />
                      <Route path="/cabinet/:companyId/service-sessions/:sessionId" element={<SessionPage />} />
                      <Route path="/cabinet/:companyId/resources" element={<ResourcesPage />} />
                      <Route path="/cabinet/:companyId/resources/new" element={<ResourceCreatePage />} />
                      <Route path="/cabinet/:companyId/resources/:serviceId" element={<ResourceEditPage />} />
                      <Route path="/cabinet/:companyId/schedule" element={<SchedulePage />} />
                      <Route path="/cabinet/:companyId/settings" element={<SettingsPage />} />
                      <Route path="/cabinet/:companyId/staff" element={<StaffPage />} />
                      <Route path="/cabinet/:companyId/notifications" element={<NotificationsPage />} />
                      <Route path="/cabinet/:companyId/link" element={<LinkPage />} />
                    </Route>
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

                {/* /:slug and /:slug/:resourceSlug are the company and the resource; static routes rank higher. */}
                <Route path="/:slug" element={<CompanyPage />} />
                <Route path="/:slug/:resourceSlug" element={<ResourcePage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Routes>
            </RouteErrorBoundary>
            <BaniFooter />
          </LegalGuard>
          <OwnerTermsGateModal />
          <UpdateBanner />
        </div>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
