import axios from 'axios'
import { useAuthStore } from '../store/authStore'

export const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Auth endpoints handle their own 401s inline (e.g. "wrong password" on the login form). Redirecting
// there too would force a full page reload before the form's own error message ever renders — see
// LoginPage.tsx. Everywhere else, a 401 on an authenticated request is a revoked/expired token
// (US-17), and the redirect must stay: otherwise the user is stuck looking at empty screens.
const AUTH_PATHS_WITHOUT_REDIRECT = ['/auth/login', '/auth/register']

api.interceptors.response.use(
  (r) => r,
  (err) => {
    const url: string = err.config?.url ?? ''
    const isAuthEndpoint = AUTH_PATHS_WITHOUT_REDIRECT.some((p) => url.includes(p))
    if (err.response?.status === 401 && !isAuthEndpoint) {
      useAuthStore.getState().logout()
      window.location.href = '/login'
    }
    return Promise.reject(err)
  },
)
