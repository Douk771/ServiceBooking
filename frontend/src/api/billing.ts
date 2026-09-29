import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'
import type { components as Cycle20Schemas } from '../types/api-cycle20.generated'

/**
 * Owner "Ваша подписка" screen (US-65, US-68, US-70) — API_CONTRACT_CYCLE7.md §41,
 * contracts/cycle7/openapi.yaml OwnerSubscriptionDto and friends.
 *
 * Types are re-exported from the generated `../types/api-cycle7.generated` (openapi-typescript
 * against contracts/cycle7/openapi.yaml, `npm run types:api`) rather than hand-written — see N22:
 * the hand-written copy that used to live here had drifted from the contract (invented
 * `SubscriptionStatus: 'Inactive'` instead of the real `'Free'`, and `'Purchasable'` instead of
 * `'Extra'` for OptionAvailability).
 */

type Schemas = components['schemas']

export type AvailableOptionDto = Schemas['AvailableOptionDto']
type SubscriptionRequestDto = Schemas['SubscriptionRequestDto']
export type OwnerSubscriptionDto = Schemas['OwnerSubscriptionDto']
type SubscriptionRequestInput = Schemas['SubscriptionRequestInput']

// ── Cycle 18 (trial plan) — API_CONTRACT_CYCLE18.md §362–§363. Types come from the generated
// cycle18 schema, not hand-written — same convention as the rest of this file (N22).
type Cycle18 = Cycle18Schemas['schemas']

export type TrialStateDto = Cycle18['TrialStateDto']
export type TrialWarningDto = Cycle18['TrialWarningDto']
export type TrialRefusalDto = Cycle18['TrialRefusalDto']
type TrialActivationInput = Cycle18['TrialActivationInput']
type TrialTermsAcknowledgementInput = Cycle18['TrialTermsAcknowledgementInput']
/** `OwnerSubscriptionDto` (cycle 7/17) + `trial`/`usage.overLimit*` (cycle 18, additive only —
 *  §372, "ломающих изменений нет"). Kept as an intersection rather than a second hand-written copy
 *  of the whole DTO, per the contract's own `additionalProperties: true` note. */
type OwnerSubscriptionDtoWithTrial = OwnerSubscriptionDto & Cycle18['OwnerSubscriptionDtoTrialPatch']

export const billingApi = {
  /** GET /api/billing/subscription. 404 means the caller owns no company (not an error state). */
  getSubscription: (): Promise<OwnerSubscriptionDtoWithTrial> =>
    api.get<OwnerSubscriptionDtoWithTrial>('/billing/subscription').then((r) => r.data),

  /** POST /api/billing/subscription/request — full desired composition, overwrites any pending request. */
  submitRequest: (input: SubscriptionRequestInput): Promise<SubscriptionRequestDto> =>
    api.post<SubscriptionRequestDto>('/billing/subscription/request', input).then((r) => r.data),

  /** DELETE /api/billing/subscription/request — idempotent, 204 whether or not a pending request existed. */
  cancelRequest: (): Promise<void> => api.delete('/billing/subscription/request').then(() => undefined),

  /** POST /api/billing/trial (§363). Body is the single `termsVersion` field, echoing the version
   *  shown to the owner (`activationTerms.version` from the subscription's `trial` block) — the server rejects any other
   *  value with 409 TrialTermsVersionMismatch. Returns the whole subscription DTO on success so the
   *  caller can swap in the response directly instead of refetching (§371 п.4). */
  activateTrial: (termsVersion: string): Promise<OwnerSubscriptionDtoWithTrial> =>
    api
      .post<OwnerSubscriptionDtoWithTrial>('/billing/trial', { termsVersion } satisfies TrialActivationInput)
      .then((r) => r.data),

  /** POST /api/billing/trial/terms-acknowledgement (§363.1) — only needed when a SuperAdmin granted
   *  the trial and the owner hasn't seen the terms yet (`activationTerms.acknowledgementRequired`). */
  acknowledgeTerms: (termsVersion: string): Promise<TrialStateDto> =>
    api
      .post<TrialStateDto>('/billing/trial/terms-acknowledgement', { termsVersion } satisfies TrialTermsAcknowledgementInput)
      .then((r) => r.data),

  /** GET /api/billing/operator-details (§432.8, NEW) — held-account only; 404 = no billing account
   *  at all (same "not an error state" convention as `getSubscription`/`getTrial` above). */
  getOperatorDetails: (): Promise<ConsentOperatorDetailsDto> =>
    api.get<ConsentOperatorDetailsDto>('/billing/operator-details').then((r) => r.data),

  /** PUT /api/billing/operator-details — empty string / whitespace-only is sent through as-is; the
   *  server treats it as `null` (§432.8), so the caller doesn't need to pre-convert. */
  updateOperatorDetails: (input: ConsentOperatorDetailsInput): Promise<ConsentOperatorDetailsDto> =>
    api.put<ConsentOperatorDetailsDto>('/billing/operator-details', input).then((r) => r.data),
}

// ── Cycle 20 (US-20-01, Т20-04 п. 3) — operator-of-record details for the paper consent form. ──────
type Cycle20 = Cycle20Schemas['schemas']
export type ConsentOperatorDetailsDto = Cycle20['ConsentOperatorDetailsDto']
export type ConsentOperatorDetailsInput = Cycle20['ConsentOperatorDetailsInput']
