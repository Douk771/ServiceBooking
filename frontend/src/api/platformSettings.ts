import { api } from './client'
import type { AdminChannelDto, AdminChannelSummary, PlatformSettings, Paged, LegalReadiness } from '../types'

// API_CONTRACT_CYCLE4.md §34 — SuperAdmin-only.
export const adminNotificationsApi = {
  listChannels: (params: { state?: string; paymentState?: string; page?: number; pageSize?: number }) =>
    api.get<Paged<AdminChannelDto>>('/admin/notification-channels', { params }).then((r) => r.data),
  summary: () => api.get<AdminChannelSummary>('/admin/notification-channels/summary').then((r) => r.data),
  // markPayment removed — POST /admin/notification-channels/{id}/payment is 410 Gone in cycle 7.
  // Replacement: adminBillingApi.assignSubscription (billing-accounts subscription options).
  suspend: (id: string, comment?: string) => api.post<void>(`/admin/notification-channels/${id}/suspend`, { comment }),
  resume: (id: string, comment?: string) => api.post<void>(`/admin/notification-channels/${id}/resume`, { comment }),
  getSettings: () => api.get<PlatformSettings>('/admin/platform-settings').then((r) => r.data),
  updateSettings: (data: PlatformSettings) =>
    api.put<PlatformSettings>('/admin/platform-settings', data).then((r) => r.data),
}

// API_CONTRACT_CYCLE11.md §116 — SuperAdmin, read-only, no-store. Diagnostic screen, not content:
// never cached by react-query beyond the default, and refetched on demand.
export const adminLegalApi = {
  getReadiness: () => api.get<LegalReadiness>('/admin/legal/readiness').then((r) => r.data),
}
