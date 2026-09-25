import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'

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

export type SubscriptionStatus = Schemas['SubscriptionStatus']
export type OptionKind = Schemas['OptionKind']
export type OptionStatus = Schemas['OptionStatus']
export type OptionAvailability = Schemas['OptionAvailability']
export type SubscriptionRequestStatus = Schemas['SubscriptionRequestStatus']

export type SubscribedPlanDto = Schemas['SubscribedPlanDto']
export type SubscribedOptionDto = Schemas['SubscribedOptionDto']
export type AvailableOptionDto = Schemas['AvailableOptionDto']
export type CoveredCompanyDto = Schemas['CoveredCompanyDto']
export type SubscriptionUsageDto = Schemas['SubscriptionUsageDto']
export type SubscriptionWarningDto = Schemas['SubscriptionWarningDto']
export type SubscriptionRequestItemDto = Schemas['SubscriptionRequestItemDto']
export type SubscriptionRequestDto = Schemas['SubscriptionRequestDto']
export type OwnerSubscriptionDto = Schemas['OwnerSubscriptionDto']
export type RejectedRequestDto = Schemas['RejectedRequestDto']
export type RequestedOptionInput = Schemas['RequestedOptionInput']
export type SubscriptionRequestInput = Schemas['SubscriptionRequestInput']

// ── Cycle 18 (trial plan) — API_CONTRACT_CYCLE18.md §362–§363. Types come from the generated
// cycle18 schema, not hand-written — same convention as the rest of this file (N22).
type Cycle18 = Cycle18Schemas['schemas']

export type TrialStateDto = Cycle18['TrialStateDto']
export type TrialWarningDto = Cycle18['TrialWarningDto']
export type TrialRefusalDto = Cycle18['TrialRefusalDto']
export type TrialActivationInput = Cycle18['TrialActivationInput']
export type TrialTermsAcknowledgementInput = Cycle18['TrialTermsAcknowledgementInput']
/** `OwnerSubscriptionDto` (cycle 7/17) + `trial`/`usage.overLimit*` (cycle 18, additive only —
 *  §372, "ломающих изменений нет"). Kept as an intersection rather than a second hand-written copy
 *  of the whole DTO, per the contract's own `additionalProperties: true` note. */
export type OwnerSubscriptionDtoWithTrial = OwnerSubscriptionDto & Cycle18['OwnerSubscriptionDtoTrialPatch']

export const billingApi = {
  /** GET /api/billing/subscription. 404 means the caller owns no company (not an error state). */
  getSubscription: (): Promise<OwnerSubscriptionDtoWithTrial> =>
    api.get<OwnerSubscriptionDtoWithTrial>('/billing/subscription').then((r) => r.data),

  /** POST /api/billing/subscription/request — full desired composition, overwrites any pending request. */
  submitRequest: (input: SubscriptionRequestInput): Promise<SubscriptionRequestDto> =>
    api.post<SubscriptionRequestDto>('/billing/subscription/request', input).then((r) => r.data),

  /** DELETE /api/billing/subscription/request — idempotent, 204 whether or not a pending request existed. */
  cancelRequest: (): Promise<void> => api.delete('/billing/subscription/request').then(() => undefined),

  /** GET /api/billing/trial (§362) — 404 means the caller has no billing account at all (not an
   *  error state, same convention as getSubscription above). */
  getTrial: (): Promise<TrialStateDto> => api.get<TrialStateDto>('/billing/trial').then((r) => r.data),

  /** POST /api/billing/trial (§363). Body is the single `termsVersion` field, echoing the version
   *  shown to the owner (`activationTerms.version` from getTrial) — the server rejects any other
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
}
