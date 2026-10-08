import { api } from './client'
import type { components } from '../types/api-cycle7.generated'
import type { components as Cycle18Schemas } from '../types/api-cycle18.generated'
import type { components as Cycle19Schemas } from '../types/api-cycle19.generated'
import type { components as Cycle20Schemas } from '../types/api-cycle20.generated'
import type { components as Cycle24Schemas } from '../types/api-cycle24.generated'
import type { components as Cycle28Schemas } from '../types/api-cycle28.generated'
import type { ShowcaseFilter } from '../utils/showcaseFilter'
import type { SubscriptionRequestDto } from './billing'

type Schemas = components['schemas']
type Cycle18 = Cycle18Schemas['schemas']
type Cycle19 = Cycle19Schemas['schemas']
type Cycle20 = Cycle20Schemas['schemas']
type Cycle24 = Cycle24Schemas['schemas']
type Cycle28 = Cycle28Schemas['schemas']

/** Cycle 24 (API_CONTRACT_CYCLE24.md §485.3) — the account's «Заказы» subscription block and the request line. Additive. */
export type AdminOrdersSubscription = Cycle24['AdminOrdersSubscriptionDto']
export type BillingLine = 'Services' | 'Orders' | 'Stays'
/** Cycle 37 (API_CONTRACT_CYCLE37.md §37.21.4): the account's «Дома» subscription block. */
export interface AdminStaysSubscription {
  planId?: string | null
  planName: string
  paidUntil?: string | null
  isActive: boolean
  housesPublished: number
  maxHouses?: number | null
}

export type TrialAccountFilter = Cycle18['TrialAccountFilter']
type TrialRegrantInput = Cycle18['TrialRegrantInput']

/** Cycle 7 shapes + cycle 18 `trial`/`trialState`/`trialEndsAt` (additive — §369, §372) + cycle 19
 *  `pendingRequest` overridden to the cycle19 shape (`items[].retired`/`retiredOptionsNotice`,
 *  API_CONTRACT_CYCLE19.md §407). */
export type AdminBillingAccountListItem = Schemas['AdminBillingAccountListItemDto'] &
  Cycle18['AdminBillingAccountListItemTrialPatch'] &
  // Cycle 28 (API_CONTRACT_CYCLE28.md §594.2) — the list item gets `isShowcase`; optional so an older server still type-checks.
  Partial<Pick<Cycle28['AdminBillingAccountItem'], 'isShowcase'>>
export type AdminBillingAccount = Omit<Schemas['AdminBillingAccountDto'], 'pendingRequest'> &
  Cycle18['AdminBillingAccountDtoTrialPatch'] & { pendingRequest?: SubscriptionRequestDto | null; ordersSubscription?: AdminOrdersSubscription | null; staysSubscription?: AdminStaysSubscription | null }
export type AdminSubscribedOption = Schemas['AdminSubscribedOptionDto']
/** Cycle 20 (US-20-02, API_CONTRACT_CYCLE20.md §433.1) adds `reasonCode`/`reasonDetails` in the
 *  request body — same `AssignOptionInput`/log shapes otherwise. */
export type AssignSubscriptionInput = Omit<Cycle20['AssignSubscriptionInput'], 'line'> & { line?: BillingLine }
export type AssignOptionInput = Schemas['AssignOptionInput']
export type SubscriptionChangeReason = Cycle20['SubscriptionChangeReason']
type SubscriptionChangeReasonDto = Cycle20['SubscriptionChangeReasonDto']
/** History item — existing shape plus `reasonCode`/`reasonTitle`/`reasonDetails` (§433.2), always
 *  `null` on rows that predate the cycle or weren't a manual hidden-plan assignment. */
type SubscriptionChangeLog = Schemas['SubscriptionChangeLogDto'] & Cycle20['SubscriptionHistoryItemReasonPatch']
// ARCHITECTURE_CYCLE19.md FE-3, API_CONTRACT_CYCLE19.md §407 — `items[].retired` and
// `retiredOptionsNotice` are new; read straight off the cycle19 schema (N22 convention).
export type AdminSubscriptionRequest = Cycle19['AdminSubscriptionRequestDtoCycle19'] & { line?: BillingLine }
export type SubscriptionStatus = Schemas['SubscriptionStatus']
type SubscriptionRequestStatus = Schemas['SubscriptionRequestStatus']

/** Cycle-3 pagination envelope (`page`/`pageSize`/`totalCount`) used by every /admin/billing-*
 *  list endpoint in this cycle — distinct from the older `total`/`hasNext` envelope elsewhere in
 *  the admin API, so it's adapted to the shared <Pagination> component's shape at the call site. */
interface PagedResult<T> {
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
    /** Cycle 28 (§594.1) — `all` (default, not sent) / `only` / `exclude`. */
    showcase?: ShowcaseFilter
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

  /** GET /api/admin/subscription-change-reasons (§433.3, NEW) — closed list of reason codes; the
   *  frontend must never hardcode its own titles/rules. Only `assignableManually: true` entries belong
   *  in the assignment form's dropdown — `TrialReissue` is included ONLY so history rows sharing the
   *  same code render with the same title, never as a selectable option there. */
  getChangeReasons: () =>
    api.get<{ items: SubscriptionChangeReasonDto[] }>('/admin/subscription-change-reasons').then((r) => r.data.items),

  listRequests: (params: { status?: SubscriptionRequestStatus; page?: number; pageSize?: number }) =>
    api.get<PagedResult<AdminSubscriptionRequest>>('/admin/subscription-requests', { params }).then((r) => r.data),

  rejectRequest: (id: string, comment?: string) =>
    api.post<void>(`/admin/subscription-requests/${id}/reject`, comment ? { comment } : {}),
}
