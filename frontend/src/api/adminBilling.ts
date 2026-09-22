import { api } from './client'
import type { components } from '../types/api-cycle7.generated'

type Schemas = components['schemas']

export type AdminBillingAccountListItem = Schemas['AdminBillingAccountListItemDto']
export type AdminBillingAccount = Schemas['AdminBillingAccountDto']
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
  listAccounts: (params: { search?: string; status?: SubscriptionStatus; page?: number; pageSize?: number }) =>
    api.get<PagedResult<AdminBillingAccountListItem>>('/admin/billing-accounts', { params }).then((r) => r.data),

  getAccount: (accountId: string) =>
    api.get<AdminBillingAccount>(`/admin/billing-accounts/${accountId}`).then((r) => r.data),

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
