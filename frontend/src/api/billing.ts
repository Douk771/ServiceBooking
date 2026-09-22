import { api } from './client'
import type { components } from '../types/api-cycle5.generated'

/**
 * Owner "Ваша подписка" screen (US-65, US-68, US-70) — API_CONTRACT_CYCLE5.md §41,
 * contracts/openapi-cycle5.yaml OwnerSubscriptionDto and friends.
 *
 * Types are re-exported from the generated `../types/api-cycle5.generated` (openapi-typescript
 * against contracts/openapi-cycle5.yaml, `npm run types:api`) rather than hand-written — see N22:
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
export type RequestedOptionInput = Schemas['RequestedOptionInput']
export type SubscriptionRequestInput = Schemas['SubscriptionRequestInput']

export const billingApi = {
  /** GET /api/billing/subscription. 404 means the caller owns no company (not an error state). */
  getSubscription: (): Promise<OwnerSubscriptionDto> => api.get<OwnerSubscriptionDto>('/billing/subscription').then((r) => r.data),

  /** POST /api/billing/subscription/request — full desired composition, overwrites any pending request. */
  submitRequest: (input: SubscriptionRequestInput): Promise<SubscriptionRequestDto> =>
    api.post<SubscriptionRequestDto>('/billing/subscription/request', input).then((r) => r.data),

  /** DELETE /api/billing/subscription/request — idempotent, 204 whether or not a pending request existed. */
  cancelRequest: (): Promise<void> => api.delete('/billing/subscription/request').then(() => undefined),
}
