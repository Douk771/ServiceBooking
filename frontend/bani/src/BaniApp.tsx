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
import { PendingPage } from './pages/PendingPage'
import { ProfilePage } from './pages/ProfilePage'
import { ResourcePage } from './pages/ResourcePage'
import { OrderPage } from './pages/OrderPage'
import { MyBookingsPage } from './pages/MyBookingsPage'

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
 *
 * Screens of FE-42-3/4/5 are <PendingPage/> until their tasks land; each owner swaps the element.
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
                  <Route path="/cabinet" element={<PendingPage title="Кабинет" />} />
                  <Route path="/cabinet/new" element={<PendingPage title="Новая компания" />} />
                  <Route path="/cabinet/subscription" element={<PendingPage title="Подписка" />} />
                  <Route path="/cabinet/:companyId" element={<PendingPage title="Кабинет" />} />
                  <Route path="/cabinet/:companyId/service-day/:date" element={<PendingPage title="День" />} />
                  <Route path="/cabinet/:companyId/orders" element={<PendingPage title="Брони" />} />
                  <Route path="/cabinet/:companyId/service-sessions/:sessionId" element={<PendingPage title="Сеанс" />} />
                  <Route path="/cabinet/:companyId/resources" element={<PendingPage title="Ресурсы" />} />
                  <Route path="/cabinet/:companyId/resources/new" element={<PendingPage title="Новый ресурс" />} />
                  <Route path="/cabinet/:companyId/resources/:serviceId" element={<PendingPage title="Ресурс" />} />
                  <Route path="/cabinet/:companyId/schedule" element={<PendingPage title="Расписание" />} />
                  <Route path="/cabinet/:companyId/settings" element={<PendingPage title="Настройки" />} />
                  <Route path="/cabinet/:companyId/staff" element={<PendingPage title="Персонал" />} />
                  <Route path="/cabinet/:companyId/notifications" element={<PendingPage title="Уведомления" />} />
                  <Route path="/cabinet/:companyId/link" element={<PendingPage title="Ссылка" />} />
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
