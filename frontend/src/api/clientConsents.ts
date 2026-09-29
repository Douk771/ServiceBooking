import { api } from './client'
import type { components } from '../types/api-cycle20.generated'
import type { HealthNoteDto, PhotoConsentStatus } from '../types'

type Cycle20 = components['schemas']

export type HealthConsentFormDto = Cycle20['HealthConsentFormDto']
export type WrittenHealthConsentStateDto = Cycle20['WrittenHealthConsentStateDto']
export type WrittenHealthConsentRevokeReason = Cycle20['WrittenHealthConsentRevokeReason']
export type WrittenHealthConsentRevokeResultDto = Cycle20['WrittenHealthConsentRevokeResultDto']

/**
 * Cycle 5 — salon-side consents scoped to a `(companyId, clientKey)` pair: photofixation consent
 * (§44) and the special-category health/contraindication note (§45). `clientKey` is a `userId` or a
 * canonical `phone:79991234567` string, exactly as the booking/notes screens already key clients.
 *
 * Cycle 20 (LG1, API_CONTRACT_CYCLE20.md §432) — the health note is gated ONLY by the written
 * (paper-form) consent mark from here on: `getHealthConsentForm`/`markWrittenConsent`/
 * `revokeWrittenConsent` replace `confirmHealthConsent`, which now answers 410 Gone
 * (`confirmHealthConsentGone` kept only so a stray old call site gets a typed 410, not a silent 404).
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

  /** §432.2 — 400 with a `RequiredConsentDto` JSON body (`requiredTextKey: "HealthDataWrittenConsentForm"`
   *  since cycle 20) when the written-consent mark is missing; the caller shows the print/mark flow and
   *  retries, same convention as before the cycle. */
  updateHealthNote: (companyId: string, clientKey: string, value: string) =>
    api
      .put<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-note`, { value })
      .then((r) => r.data),

  /** §432.3 — unchanged: idempotent 200 even with no mark at all (deletion never needs the consent it
   *  reduces). */
  deleteHealthNote: (companyId: string, clientKey: string) =>
    api.delete<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-note`),

  /** §432.4 — NEW. Values for the printable paper-form page: a fresh `formId` on every call (never
   *  persisted server-side before the mark), the current form text version, and the runtime values for
   *  `applyLegalRuntimeValues`. `Cache-Control: no-store` on the response — never cache this client-side. */
  getHealthConsentForm: (companyId: string, clientKey: string) =>
    api
      .get<HealthConsentFormDto>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-consent-form`, {
        headers: { 'Cache-Control': 'no-store' },
      })
      .then((r) => r.data),

  /** §432.5 — NEW. Marks "written consent obtained". `textVersion` must match the CURRENT form
   *  version (409 otherwise — "reprint and mark again"); `formId` is `null` for the salon's own form. */
  markWrittenConsent: (companyId: string, clientKey: string, input: { textVersion: string; formId: string | null; confirmed: true }) =>
    api
      .post<WrittenHealthConsentStateDto>(
        `/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-written-consent`,
        input,
      )
      .then((r) => r.data),

  /** §432.6 — NEW. Lifts every live mark for this client in this company AND deletes the health note
   *  in the same transaction — the caller must warn about the irreversible deletion BEFORE calling this. */
  revokeWrittenConsent: (companyId: string, clientKey: string, reason: WrittenHealthConsentRevokeReason) =>
    api
      .post<WrittenHealthConsentRevokeResultDto>(
        `/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-written-consent/revoke`,
        { reason },
      )
      .then((r) => r.data),

  /** §432.7 — the salon's electronic consent route is withdrawn (LG1): always 410 Gone now. Kept as a
   *  typed call only for the rare stray reference; no screen in this cycle calls it on purpose. */
  confirmHealthConsentGone: (companyId: string, clientKey: string) =>
    api.post<void>(`/companies/${companyId}/clients/${encodeURIComponent(clientKey)}/health-consent`, {}),
}
