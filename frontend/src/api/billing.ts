import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'
import type { components as Cycle19Schemas } from '../types/api-cycle19.generated'
import type { components as Cycle20Schemas } from '../types/api-cycle20.generated'
import type { components as Cycle24Schemas } from '../types/api-cycle24.generated'
import type { components as Cycle37Schemas } from '../types/api-cycle37.generated'

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
// ARCHITECTURE_CYCLE19.md §388.5/FE-4, API_CONTRACT_CYCLE19.md §408 — `items[].retired` and
// `retiredOptionsNotice` are new (breaking-additive) fields on the request DTO; read straight off
// the cycle19 schema rather than the cycle7 one (N22 convention).
type Cycle19 = Cycle19Schemas['schemas']
export type SubscriptionRequestDto = Cycle19['SubscriptionRequestDto']
/** `OwnerSubscriptionDto` (cycle 7) with `pendingRequest` overridden to the cycle19 shape — the
 *  cycle7 schema's own nested `pendingRequest` type predates `retired`/`retiredOptionsNotice`. */
export type OwnerSubscriptionDto = Omit<Schemas['OwnerSubscriptionDto'], 'pendingRequest'> & {
  pendingRequest?: SubscriptionRequestDto | null
}
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

// ── Cycle 24 (ARCHITECTURE_CYCLE24.md §462.2 п.2, API_CONTRACT_CYCLE24.md §485.1) — the same screen serves the goods
// «Заказы» line. Additive: `line`, `orders`, `availablePlans`. Without `line` everything below behaves exactly as before.
type Cycle24 = Cycle24Schemas['schemas']
// Cycle 37 (API_CONTRACT_CYCLE37.md §37.21.4): a third line — `Stays` — adds the `stays` block; `availablePlans` lists the line's plans.
type Cycle37 = Cycle37Schemas['schemas']
export type BillingLine = Cycle37['CompanyKind'] | 'Baths'
export type OrdersUsageDto = Cycle24['OrdersUsageDto']
export type StaysSubscriptionBlockDto = Cycle37['StaysSubscriptionBlockDto']
/** Cycle 42 (API_CONTRACT_CYCLE42.md §42.21): `Baths` adds the `baths` block; each available plan of the line carries `maxResources`. */
export interface BathsSubscriptionBlockDto {
  resourcesPublished: number
  maxResources?: number | null
  isTrial: boolean
  trialEndsAtUtc?: string | null
  warningLevel: Cycle37['StaysSubscriptionBlockDto']['warningLevel']
  text?: string | null
}
export type AvailablePlanDto = Cycle24['AvailablePlanDto'] & { maxResources?: number | null }
export type OwnerSubscriptionDtoCycle24Patch = Pick<Cycle24['OwnerSubscriptionDtoCycle24'], 'line' | 'orders' | 'availablePlans'> & { stays?: StaysSubscriptionBlockDto | null; baths?: BathsSubscriptionBlockDto | null }

export const billingApi = {
  /** GET /api/billing/subscription. 404 means the caller owns no company (not an error state). */
  getSubscription: (line?: BillingLine): Promise<OwnerSubscriptionDtoWithTrial & Partial<OwnerSubscriptionDtoCycle24Patch>> => {
    type Body = OwnerSubscriptionDtoWithTrial & Partial<OwnerSubscriptionDtoCycle24Patch>
    // ezbook never sends `line` (server default `Services`): that request is exactly the pre-cycle-24 one, no config object.
    const request = line && line !== 'Services' ? api.get<Body>('/billing/subscription', { params: { line } }) : api.get<Body>('/billing/subscription')
    return request.then((r) => r.data)
  },

  /** POST /api/billing/subscription/request — full desired composition, overwrites any pending request. */
  submitRequest: (input: SubscriptionRequestInput & { line?: BillingLine; planId?: string }): Promise<SubscriptionRequestDto> =>
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
