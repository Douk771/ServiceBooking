import { api } from './client'
import type { AuthResponse } from '../types'

export const authApi = {
  login: (phone: string, password: string) =>
    api.post<AuthResponse>('/auth/login', { phone, password }).then((r) => r.data),

  /** API_CONTRACT_CYCLE5.md §40 (BREAKING № 2) — `acceptedLegal` is gone; a request without `legal`
   *  or missing either version is a 400. `PdnConsent` is deliberately NOT part of this call — it's a
   *  separate, non-blocking request (§41.2) made after this one succeeds, so the two-consent split
   *  required by ст. 9 is enforced by the protocol shape, not just by the form's layout. */
  register: (data: {
    firstName: string
    lastName: string
    phone: string
    password: string
    email?: string
    legal: { privacyAcknowledgedVersion: string; termsAcceptedVersion: string }
  }) => api.post<AuthResponse>('/auth/register', data).then((r) => r.data),
}
