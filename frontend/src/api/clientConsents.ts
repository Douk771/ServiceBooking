import { api } from './client'
import type { HealthNoteDto, PhotoConsentStatus } from '../types'

/**
 * Cycle 5 — salon-side consents scoped to a `(companyId, clientKey)` pair: photofixation consent
 * (§44) and the special-category health/contraindication note (§45). `clientKey` is a `userId` or a
 * canonical `phone:79991234567` string, exactly as the booking/notes screens already key clients.
 */
export const clientConsentsApi = {
  getPhotoConsent: (companyId: string, clientKey: string) =>
    api
      .get<PhotoConsentStatus>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/photo-consent`)
      .then((r) => r.data),

  /** §44.2 — recorded by a staff member on the client's behalf; `confirmed: false` is rejected (400). */
  confirmPhotoConsent: (companyId: string, clientKey: string, textVersion: string) =>
    api
      .post<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/photo-consent`, {
        textVersion,
        confirmed: true,
      })
      .then((r) => r.data),

  getHealthNote: (companyId: string, clientKey: string) =>
    api
      .get<HealthNoteDto>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-note`)
      .then((r) => r.data),

  /** §45.2 — 400 with `{requiredTextKey: "HealthDataConsent"}` when consent is missing; the caller
   *  is expected to show the consent form and retry, not to treat this as a generic failure. */
  updateHealthNote: (companyId: string, clientKey: string, value: string) =>
    api
      .put<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-note`, { value })
      .then((r) => r.data),

  deleteHealthNote: (companyId: string, clientKey: string) =>
    api.delete<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-note`),

  confirmHealthConsent: (companyId: string, clientKey: string, textVersion: string) =>
    api
      .post<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-consent`, {
        textVersion,
        confirmed: true,
      })
      .then((r) => r.data),
}
