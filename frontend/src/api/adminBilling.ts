import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'

type Schemas = components['schemas']
type Cycle18 = Cycle18Schemas['schemas']

export type TrialAccountFilter = Cycle18['TrialAccountFilter']
export type AdminAccountTrialDto = Cycle18['AdminAccountTrialDto']
export type AdminTrialGrantDto = Cycle18['AdminTrialGrantDto']
export type TrialRegrantInput = Cycle18['TrialRegrantInput']
export type TrialRefusalDto = Cycle18['TrialRefusalDto']

/** Cycle 7 shapes + cycle 18 `trial`/`trialState`/`trialEndsAt` (additive — §369, §372). */
export type AdminBillingAccountListItem = Schemas['AdminBillingAccountListItemDto'] & Cycle18['AdminBillingAccountListItemTrialPatch']
export type AdminBillingAccount = Schemas['AdminBillingAccountDto'] & Cycle18['AdminBillingAccountDtoTrialPatch']
export type AdminSubscribedOption = Schemas['AdminSubscribedOptionDto']
export type AdminAccountCompany = Schemas['AdminAccountCompanyDto']
export type AdminAccountChannel = Schemas['AdminAccountChannelDto']
export type AssignSubscriptionInput = Schemas['AssignSubscriptionInput']
export type AssignOptionInput = Schemas['AssignOptionInput']
export type SubscriptionChangeLog = Schemas['SubscriptionChangeLogDto']
export type AdminSubscriptionRequest = Schemas['AdminSubscriptionRequestDto']
export type SubscriptionStatus = Schemas['SubscriptionStatus']
export type SubscriptionRequestStatus = Schemas['SubscriptionRequestStatus']

/** Cycle-3 pagination envelope (`page`/`pageSize`/`totalCount`) used by every /admin/billing-*
 *  list endpoint in this cycle — distinct from the older `total`/`hasNext` envelope elsewhere in
 *  the admin API, so it's adapted to the shared <Pagination> component's shape at the call site. */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

// API_CONTRACT_CYCLE7.md §49, §53 — SuperAdmin-only. Replaces the 410'd
// PUT /admin/owners/{ownerUserId}/subscription and POST /admin/notification-channels/{id}/payment.
export const adminBillingApi = {
  // §369 — `?trial=` is a new, optional filter param; omitting it keeps prior behaviour unchanged.
  listAccounts: (params: {
    search?: string
    status?: SubscriptionStatus
    trial?: TrialAccountFilter
    page?: number
    pageSize?: number
  }) => api.get<PagedResult<AdminBillingAccountListItem>>('/admin/billing-accounts', { params }).then((r) => r.data),

  getAccount: (accountId: string) =>
    api.get<AdminBillingAccount>(`/admin/billing-accounts/${accountId}`).then((r) => r.data),

  /** POST /api/admin/billing-accounts/{accountId}/trial (§368) — same checks as the owner's own
   *  self-service activation; body-less. 409 carries TrialRefusalDto, same 9 codes as §364. */
  grantTrial: (accountId: string) =>
    api.post<AdminBillingAccount>(`/admin/billing-accounts/${accountId}/trial`).then((r) => r.data),

  /** POST /api/admin/billing-accounts/{accountId}/trial/regrant (§368) — separate route on purpose
   *  (Д1-бис): the emergency bypass must not be reachable via a stray field on the ordinary grant.
   *  `reason` is mandatory and non-empty (server-enforced 400, client validation is a courtesy only). */
  regrantTrial: (accountId: string, reason: string) =>
    api
      .post<AdminBillingAccount>(`/admin/billing-accounts/${accountId}/trial/regrant`, { reason } satisfies TrialRegrantInput)
      .then((r) => r.data),

  assignSubscription: (accountId: string, data: AssignSubscriptionInput) =>
    api.put<AdminBillingAccount>(`/admin/billing-accounts/${accountId}/subscription`, data).then((r) => r.data),

  getHistory: (accountId: string) =>
    api
      .get<{ items: SubscriptionChangeLog[] }>(`/admin/billing-accounts/${accountId}/subscription-history`)
      .then((r) => r.data.items),

  listRequests: (params: { status?: SubscriptionRequestStatus; page?: number; pageSize?: number }) =>
    api.get<PagedResult<AdminSubscriptionRequest>>('/admin/subscription-requests', { params }).then((r) => r.data),

  rejectRequest: (id: string, comment?: string) =>
    api.post<void>(`/admin/subscription-requests/${id}/reject`, comment ? { comment } : {}),
}
