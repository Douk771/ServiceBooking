import { api } from './client'
import type { AdminChannelDto, AdminChannelSummary, PlatformSettings, NotificationTransport, Paged } from '../types'

// API_CONTRACT_CYCLE4.md §34 — SuperAdmin-only.
export const adminNotificationsApi = {
  listChannels: (params: {
    state?: string
    paymentState?: string
    page?: number
    pageSize?: number
    /** API_CONTRACT_CYCLE9.md §114.3 — new ?transport= filter. */
    transport?: NotificationTransport
  }) => api.get<Paged<AdminChannelDto>>('/admin/notification-channels', { params }).then((r) => r.data),
  summary: () => api.get<AdminChannelSummary>('/admin/notification-channels/summary').then((r) => r.data),
  // markPayment removed — POST /admin/notification-channels/{id}/payment is 410 Gone in cycle 7.
  // Replacement: adminBillingApi.assignSubscription (billing-accounts subscription options).
  suspend: (id: string, comment?: string) => api.post<void>(`/admin/notification-channels/${id}/suspend`, { comment }),
  resume: (id: string, comment?: string) => api.post<void>(`/admin/notification-channels/${id}/resume`, { comment }),
  getSettings: () => api.get<PlatformSettings>('/admin/platform-settings').then((r) => r.data),
  updateSettings: (data: PlatformSettings) =>
    api.put<PlatformSettings>('/admin/platform-settings', data).then((r) => r.data),
}
