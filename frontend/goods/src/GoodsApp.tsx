import type { ReactNode } from 'react'
import { BrowserRouter, Routes, Route, Navigate, Outlet, useLocation } from 'react-router-dom'
import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { queryClient } from '@/queryClient'
import { useAuthStore } from '@/store/authStore'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { LegalGuard } from '@/components/legal/LegalGuard'
import { NoticeLinkProvider } from '@/components/legal/NoticeLink'
import { OwnerTermsGateModal } from '@/components/legal/OwnerTermsGateModal'
import { LoginPage } from '@/pages/LoginPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { ConsentsPage } from '@/pages/ConsentsPage'
import { SubjectRequestPage } from '@/pages/SubjectRequestPage'
import { LegalDocumentPage } from '@/pages/LegalDocumentPage'
import { NoticesPage } from '@/pages/NoticesPage'
import { GoodsNavbar } from './components/GoodsNavbar'
import { GoodsFooter } from './components/GoodsFooter'
import { LandingPage } from './pages/LandingPage'
import { StorefrontPage } from './pages/StorefrontPage'
import { OrderPage } from './pages/OrderPage'
import { MyOrdersPage } from './pages/MyOrdersPage'
import { GoodsProfilePage } from './pages/GoodsProfilePage'
import { NotFoundPage } from './pages/NotFoundPage'
import { CabinetHomePage } from './pages/cabinet/CabinetHomePage'
import { CreateShopPage } from './pages/cabinet/CreateShopPage'
import { ShopLayout } from './pages/cabinet/ShopLayout'
import { OrdersScreenPage } from './pages/cabinet/OrdersScreenPage'
import { CatalogPage } from './pages/cabinet/CatalogPage'
import { SettingsPage } from './pages/cabinet/SettingsPage'
import { StaffPage } from './pages/cabinet/StaffPage'
import { LinkPage } from './pages/cabinet/LinkPage'
import { HoursPage } from './pages/cabinet/HoursPage'
import { MenuPage } from './pages/cabinet/MenuPage'
import { ShopNotificationsPage } from './pages/cabinet/ShopNotificationsPage'
import { DevicesPage } from './pages/cabinet/DevicesPage'
import { SubscriptionPage } from './pages/cabinet/SubscriptionPage'
import { shopsApi } from './api/shops'

/**
 * Routes reachable while a "Material" change to a GLOBAL-gate document is pending acceptance (same reasoning
 * as ezbook's list in App.tsx, API_CONTRACT_CYCLE5.md §41/§48.4): the legal pages themselves, the data request
 * and «Мои согласия». Every legal route of goods-routes.json must be listed — checked by goodsRoutes.test.ts.
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

/**
 * Cycle 20 platform notices on goods (post-merge decision): the banner and `/notices` are shared with ezbook, but a
 * notice's `linkUrl` is an ezbook path, so it opens on the ezbook origin from `kinds-summary` (signed-in only —
 * notices themselves are signed-in only).
 */
function GoodsNoticeLinks({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated())
  const { data: summary } = useQuery({
    queryKey: ['kinds-summary'],
    queryFn: shopsApi.kindsSummary,
    enabled: isAuthenticated,
    retry: false,
  })
  return (
    <NoticeLinkProvider base={{ kind: 'external', origin: summary?.services.siteUrl ?? null }}>
      {children}
    </NoticeLinkProvider>
  )
}

/** Remounts on navigation so one crashed page does not stick after «На главную» (same pattern as ezbook, §103.1). */
function RouteErrorBoundary({ children }: { children: ReactNode }) {
  const location = useLocation()
  return <ErrorBoundary key={location.pathname}>{children}</ErrorBoundary>
}

/**
 * goods.ezbook.ru (ARCHITECTURE_CYCLE23.md §399.3). The route table mirrors contracts/cycle23/goods-routes.json
 * (`spaRoutes` + `/:slug`); goodsRoutes.test.ts checks both directions, and every first segment must be a
 * reserved word so a shop address can never shadow a route.
 */
export function GoodsApp() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <div className="min-h-screen bg-cream font-sans text-ink">
          <GoodsNavbar />
          <GoodsNoticeLinks>
            <LegalGuard bypassPaths={CONSENT_GATE_BYPASS_PATHS} showPlatformNotices>
              <RouteErrorBoundary>
                <Routes>
                  <Route path="/" element={<LandingPage />} />
                  <Route path="/login" element={<LoginPage />} />
                  <Route path="/register" element={<RegisterPage />} />
                  <Route path="/o/:token" element={<OrderPage />} />

                  <Route element={<RequireAuth />}>
                    <Route path="/orders" element={<MyOrdersPage />} />
                    <Route path="/profile" element={<GoodsProfilePage />} />
                    <Route path="/profile/consents" element={<ConsentsPage />} />
                    <Route path="/notices" element={<NoticesPage />} />
                    <Route path="/cabinet" element={<CabinetHomePage />} />
                    <Route path="/cabinet/new" element={<CreateShopPage />} />
                    <Route path="/cabinet/devices" element={<DevicesPage />} />
                    <Route path="/cabinet/subscription" element={<SubscriptionPage />} />
                    <Route element={<ShopLayout />}>
                      <Route path="/cabinet/:shopId/orders" element={<OrdersScreenPage />} />
                      <Route path="/cabinet/:shopId/catalog" element={<CatalogPage />} />
                      <Route path="/cabinet/:shopId/settings" element={<SettingsPage />} />
                      <Route path="/cabinet/:shopId/staff" element={<StaffPage />} />
                      <Route path="/cabinet/:shopId/link" element={<LinkPage />} />
                      <Route path="/cabinet/:shopId/hours" element={<HoursPage />} />
                      <Route path="/cabinet/:shopId/menu" element={<MenuPage />} />
                      <Route path="/cabinet/:shopId/notifications" element={<ShopNotificationsPage />} />
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

                  {/* /:slug is the shop page; it stays last-resort among single-segment paths (static routes rank higher). */}
                  <Route path="/:slug" element={<StorefrontPage />} />
                  <Route path="*" element={<NotFoundPage />} />
                </Routes>
              </RouteErrorBoundary>
              <GoodsFooter />
            </LegalGuard>
          </GoodsNoticeLinks>
          <OwnerTermsGateModal />
        </div>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
