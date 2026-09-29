import { api } from './client'
import type { PushConfig, PushSubscriptionDevice, StaffPushSettings } from '../types'

// API_CONTRACT_CYCLE9.md §115 — staff Web Push.
/** API_CONTRACT_CYCLE24.md §484 — which site's devices: ezbook sends nothing (server default `Services`), goods `Orders`. */
export type PushSite = 'Services' | 'Orders'

export const pushApi = {
  getConfig: (site?: PushSite) =>
    api.get<PushConfig>('/push/config', { params: site ? { site } : undefined }).then((r) => r.data),

  // §115.2 — currentEndpoint is optional; without it the server always answers isCurrent: false.
  listSubscriptions: (currentEndpoint?: string, site?: PushSite) =>
    api
      .get<{ items: PushSubscriptionDevice[] }>('/push/subscriptions', {
        params: currentEndpoint || site ? { ...(currentEndpoint ? { currentEndpoint } : {}), ...(site ? { site } : {}) } : undefined,
      })
      .then((r) => r.data.items),

  subscribe: (input: { endpoint: string; keys: { p256dh: string; auth: string }; deviceLabel?: string; site?: PushSite }) =>
    api.post<PushSubscriptionDevice>('/push/subscriptions', input).then((r) => r.data),

  // §115.4 — deletes any of the caller's own devices, including one that isn't the current one
  // ("forgot to log out on the salon's shared computer").
  deleteSubscription: (id: string) => api.delete<void>(`/push/subscriptions/${id}`),

  // §105.5 rubezh 2 / §115.4 — best-effort call on logout. Idempotent: 204 even if the row is already
  // gone.
  deleteCurrent: (endpoint: string) => api.delete<void>('/push/subscriptions/current', { data: { endpoint } }),

  getCompanySettings: (companyId: string) =>
    api.get<StaffPushSettings>(`/companies/${companyId}/staff-push-settings`).then((r) => r.data),
  updateCompanySettings: (companyId: string, staffPushEnabled: boolean) =>
    api.put<StaffPushSettings>(`/companies/${companyId}/staff-push-settings`, { staffPushEnabled }).then((r) => r.data),
}
