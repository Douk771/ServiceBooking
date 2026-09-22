import { api } from './client'
import type { ConsentPurpose, ConsentRevokeEffects, ConsentRevokeResponse, ProfileConsentsResponse } from '../types'

/**
 * Cycle 5 — `POST /api/profile/consents` (granular `PdnConsent` purposes) and its revoke pair
 * (API_CONTRACT_CYCLE5.md §41). All three are `[Authorize]` and in the `LegalConsentFilter`
 * allow-list, so they stay reachable from ConsentGate even while a Material document is pending.
 */
export const consentsApi = {
  get: () => api.get<ProfileConsentsResponse>('/profile/consents').then((r) => r.data),

  /** §41.2 — an empty `purposes` array is a valid request: it records "shown the form, consented to
   *  nothing" without blocking anything. */
  grant: (documentKey: 'PdnConsent', version: string, purposes: ConsentPurpose[]) =>
    api.post<ProfileConsentsResponse>('/profile/consents', { documentKey, version, purposes }).then((r) => r.data),

  /** §41.3 — `purpose: null` revokes the whole document (every purpose at once). */
  revoke: (documentKey: 'PdnConsent', purpose: ConsentPurpose | null, reason?: string) =>
    api
      .post<ConsentRevokeResponse>('/profile/consents/revoke', { documentKey, purpose, reason })
      .then((r) => r.data),

  /** §41.3 — dry run: same `effects` shape as `revoke`, nothing is changed. Lets the confirmation
   *  screen say "12 photos will be deleted" instead of a vague warning. */
  revokePreview: (purpose: ConsentPurpose) =>
    api
      .get<ConsentRevokeEffects>('/profile/consents/revoke-preview', { params: { purpose } })
      .then((r) => r.data),
}
