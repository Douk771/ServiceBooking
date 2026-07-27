import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
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
import { useAuthStore } from './store/authStore'

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
})

function ProtectedRoute({ children, roles }: { children: React.ReactNode; roles?: string[] }) {
  const { isAuthenticated, hasRole } = useAuthStore()
  if (!isAuthenticated()) return <Navigate to="/login" replace />
  if (roles && !roles.some(hasRole)) return <Navigate to="/" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          {/* Embed route — no Navbar */}
          <Route path="/embed/:slug" element={
            <div className="min-h-screen bg-white font-sans">
              <EmbedPage />
            </div>
          } />

          {/* All other routes with Navbar */}
          <Route path="*" element={
            <div className="min-h-screen bg-cream font-sans text-ink">
              <Navbar />
              <Routes>
                <Route path="/" element={<HomePage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/register" element={<RegisterPage />} />
                <Route path="/company/:slug" element={<CompanyPage />} />
                <Route path="/my-bookings" element={
                  <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']}>
                    <MyBookingsPage />
                  </ProtectedRoute>
                } />
                <Route path="/my-visits" element={
                  <ProtectedRoute roles={['Client', 'Master', 'CompanyOwner', 'SuperAdmin']}>
                    <ClientBookingsPage />
                  </ProtectedRoute>
                } />
                <Route path="/cabinet" element={
                  <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']}>
                    <CabinetPage />
                  </ProtectedRoute>
                } />
                <Route path="/owner/company/:id" element={
                  <ProtectedRoute roles={['CompanyOwner', 'SuperAdmin']}>
                    <CompanyManagePage />
                  </ProtectedRoute>
                } />
                <Route path="/profile" element={
                  <ProtectedRoute><ProfilePage /></ProtectedRoute>
                } />
                <Route path="/admin" element={
                  <ProtectedRoute roles={['SuperAdmin']}>
                    <AdminPage />
                  </ProtectedRoute>
                } />
                {/* Legacy redirects */}
                <Route path="/dashboard" element={<Navigate to="/cabinet" replace />} />
                <Route path="/owner" element={<Navigate to="/cabinet" replace />} />
              </Routes>
            </div>
          } />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
