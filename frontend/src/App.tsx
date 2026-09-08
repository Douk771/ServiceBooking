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
import { AdminPage } from './pages/AdminPage'
import { LegalDocumentPage } from './pages/LegalDocumentPage'
import { DeleteAccountPage } from './pages/DeleteAccountPage'
import { Footer } from './components/layout/Footer'
import { ConsentGate } from './components/legal/ConsentGate'
import { LegalUpdateBanner } from './components/legal/LegalUpdateBanner'
import { legalApi } from './api/legal'
import { useAuthStore } from './store/authStore'
import { useLegalStore } from './store/legalStore'

function ProtectedRoute({ children, roles }: { children: React.ReactNode; roles?: string[] }) {
  const { isAuthenticated, hasRole } = useAuthStore()
  if (!isAuthenticated()) return <Navigate to="/login" replace />
  if (roles && !roles.some(hasRole)) return <Navigate to="/" replace />
  return <>{children}</>
}

// Routes reachable while a "Material" legal-document change is pending acceptance (US-37 п. 4,
// US-39 п. 9) — the same set the backend allow-lists in API_CONTRACT.md §0.4, minus the auth/health
// endpoints that have no frontend page of their own.
const CONSENT_GATE_BYPASS_PATHS = ['/privacy', '/terms', '/profile/delete']

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
                    <Route path="/privacy" element={<LegalDocumentPage type="Privacy" />} />
                    <Route path="/terms" element={<LegalDocumentPage type="Terms" />} />
                    {/* Legacy redirects */}
                    <Route path="/dashboard" element={<Navigate to="/cabinet" replace />} />
                    <Route path="/owner" element={<Navigate to="/cabinet" replace />} />
                  </Routes>
                  <Footer />
                </LegalGuard>
              </div>
            }
          />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
