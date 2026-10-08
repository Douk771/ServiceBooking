import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { withReturnTo } from '../../utils/returnTo'

/**
 * Гость уходит на /login (с `returnTo`, если передан `returnToPath`); вошедший без нужной роли — на `deniedTo` (по умолчанию `/`).
 */
export function ProtectedRoute({
  children,
  roles,
  returnToPath,
  deniedTo = '/',
}: {
  children: ReactNode
  roles?: string[]
  returnToPath?: string
  deniedTo?: string
}) {
  const { isAuthenticated, hasRole } = useAuthStore()
  if (!isAuthenticated()) return <Navigate to={withReturnTo('/login', returnToPath)} replace />
  if (roles && !roles.some(hasRole)) return <Navigate to={deniedTo} replace />
  return <>{children}</>
}
