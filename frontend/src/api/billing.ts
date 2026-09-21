import { api } from './client'

/**
 * Owner "Ваша подписка" screen (US-65, US-68, US-70) — API_CONTRACT_CYCLE5.md §41,
 * contracts/openapi-cycle5.yaml OwnerSubscriptionDto and friends. Not implemented on the backend
 * yet in this pass (grep for BillingAccount in ServiceBooking.API is currently empty); calls are
 * written strictly against the contract so they start working the moment the backend ships, with
 * no client-side reshaping needed.
 */

export type SubscriptionStatus = 'Active' | 'Expired' | 'Inactive'
export type OptionKind = 'Toggle' | 'Quantity'
export type OptionStatus = 'Active' | 'Ending'
export type OptionAvailability = 'Included' | 'Purchasable' | 'Unavailable'
export type SubscriptionRequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'

export interface SubscribedPlanDto {
  id: string | null
  name: string
  description?: string | null
  pricePerMonth: number
  includes: string[]
}

export interface SubscribedOptionDto {
  optionId: string
  name: string
  description?: string | null
  kind: OptionKind
  unitName?: string | null
  quantity: number
  pricePerUnit: number
  pricePerMonth: number
  status: OptionStatus
  statusText: string
  endsAt?: string | null
  canDisable: boolean
}

export interface AvailableOptionDto {
  optionId: string
  name: string
  description?: string | null
  kind: OptionKind
  unitName?: string | null
  pricePerMonth: number | null
  maxQuantity: number | null
  availability: OptionAvailability
  availabilityText: string
  canRequest: boolean
}

export interface CoveredCompanyDto {
  companyId: string
  companyName: string
  employeeCount: number
  hasNumber: boolean
}

export interface SubscriptionUsageDto {
  companiesUsed: number
  companiesLimit: number | null
  employeesUsed: number
  employeesLimit: number | null
  employeesText: string
  companiesText: string
  numbersPaid: number
  numbersRegistered: number
  numbersText: string
}

export interface SubscriptionWarningDto {
  kind: 'Expiring' | 'Expired'
  text: string
  affected: string[]
}

export interface SubscriptionRequestItemDto {
  optionId: string
  name: string
  quantity: number
}

export interface SubscriptionRequestDto {
  id: string
  status: SubscriptionRequestStatus
  createdAt: string
  desiredPlanId?: string | null
  desiredPlanName?: string | null
  estimatedMonthlyPrice: number
  items: SubscriptionRequestItemDto[]
  comment?: string | null
}

export interface OwnerSubscriptionDto {
  currency: string
  status: SubscriptionStatus
  statusText: string
  plan: SubscribedPlanDto
  options: SubscribedOptionDto[]
  totalMonthlyPrice: number
  paidUntil?: string | null
  expiresInDays?: number | null
  isExpiringSoon: boolean
  usage: SubscriptionUsageDto
  companies: CoveredCompanyDto[]
  warning?: SubscriptionWarningDto | null
  availableOptions: AvailableOptionDto[]
  pendingRequest?: SubscriptionRequestDto | null
  canRequestChanges: boolean
}

export interface RequestedOptionInput {
  optionId: string
  quantity: number
}

export interface SubscriptionRequestInput {
  planId?: string | null
  options: RequestedOptionInput[]
  comment?: string | null
}

export const billingApi = {
  /** GET /api/billing/subscription. 404 means the caller owns no company (not an error state). */
  getSubscription: (): Promise<OwnerSubscriptionDto> => api.get<OwnerSubscriptionDto>('/billing/subscription').then((r) => r.data),

  /** POST /api/billing/subscription/request — full desired composition, overwrites any pending request. */
  submitRequest: (input: SubscriptionRequestInput): Promise<SubscriptionRequestDto> =>
    api.post<SubscriptionRequestDto>('/billing/subscription/request', input).then((r) => r.data),

  /** DELETE /api/billing/subscription/request — idempotent, 204 whether or not a pending request existed. */
  cancelRequest: (): Promise<void> => api.delete('/billing/subscription/request').then(() => undefined),
}
