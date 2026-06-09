import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Navbar } from './components/layout/Navbar'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { RegisterPage } from './pages/RegisterPage'
import { CompanyPage } from './pages/CompanyPage'
import { MyBookingsPage } from './pages/MyBookingsPage'
import { DashboardPage } from './pages/DashboardPage'
import { OwnerPage } from './pages/owner/OwnerPage'
import { CompanyManagePage } from './pages/owner/CompanyManagePage'
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
        <div className="min-h-screen bg-gray-50 font-sans">
          <Navbar />
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/company/:slug" element={<CompanyPage />} />
            <Route path="/my-bookings" element={
              <ProtectedRoute><MyBookingsPage /></ProtectedRoute>
            } />
            <Route path="/dashboard" element={
              <ProtectedRoute roles={['Master', 'CompanyOwner', 'SuperAdmin']}>
                <DashboardPage />
              </ProtectedRoute>
            } />
            <Route path="/owner" element={
              <ProtectedRoute><OwnerPage /></ProtectedRoute>
            } />
            <Route path="/owner/company/:id" element={
              <ProtectedRoute><CompanyManagePage /></ProtectedRoute>
            } />
          </Routes>
        </div>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
