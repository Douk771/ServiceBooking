import axios from 'axios'
import { useAuthStore } from '../store/authStore'
import { useLegalStore } from '../store/legalStore'
import { useOwnerGateStore } from '../store/ownerGateStore'
import { queryClient } from '../queryClient'
import type { OwnerGate451 } from '../types'

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
    // 451 means "accept the updated legal documents before doing anything else" — it is NOT a
    // permissions error, so unlike 401 it never logs the user out (API_CONTRACT_CYCLE5.md §38.2).
    // Two different 451s exist and are told apart by Content-Type, never by the caller's endpoint:
    //   - text/plain  → the GLOBAL gate (Privacy/TermsClient) — blocks the whole app; ConsentGate
    //     takes over the screen until POST /api/legal/accept clears it with a fresh token.
    //   - application/json → the OWNER-SCOPE gate (TermsOwner) — blocks only the one action that
    //     tripped it (reading and non-owner actions keep working); OwnerTermsGateModal opens with the
    //     type/version from the body so the owner can accept and retry.
    if (err.response?.status === 451) {
      const contentType = String(err.response.headers?.['content-type'] ?? '')
      if (contentType.includes('application/json')) {
        useOwnerGateStore.getState().setPending(err.response.data as OwnerGate451)
      } else {
        useLegalStore.getState().setConsentRequired(true)
        queryClient.invalidateQueries({ queryKey: ['legal-consent-status'] })
      }
      return Promise.reject(err)
    }
    if (err.response?.status === 401 && !isAuthEndpoint) {
      useAuthStore.getState().logout()
      window.location.href = '/login'
    }
    return Promise.reject(err)
  },
)
