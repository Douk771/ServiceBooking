import { api } from './client'
import type { AuthResponse, PhoneVerificationRef } from '../types'

export const authApi = {
  login: (phone: string, password: string) =>
    api.post<AuthResponse>('/auth/login', { phone, password }).then((r) => r.data),

  /** API_CONTRACT_CYCLE5.md §40 (BREAKING № 2) — `acceptedLegal` is gone; a request without `legal`
   *  or missing either version is a 400. `PdnConsent` is deliberately NOT part of this call — it's a
   *  separate, non-blocking request (§41.2) made after this one succeeds, so the two-consent split
   *  required by ст. 9 is enforced by the protocol shape, not just by the form's layout.
   *
   *  `phoneVerification` (API_CONTRACT_CYCLE14.md §168) is NEW and optional — omitting it keeps this
   *  call byte-for-byte what it was before cycle 14 (US-14-07); a stale/invalid session for the
   *  phone actually submitted is rejected server-side with its own 409, so the caller doesn't have to
   *  duplicate that check here. */
  register: (data: {
    firstName: string
    lastName: string
    phone: string
    password: string
    email?: string
    legal: { privacyAcknowledgedVersion: string; termsAcceptedVersion: string }
    phoneVerification?: PhoneVerificationRef
  }) => api.post<AuthResponse>('/auth/register', data).then((r) => r.data),
}
