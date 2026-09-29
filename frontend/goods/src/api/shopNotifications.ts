import { api } from '@/api/client'
import type { ShopNotificationSettingsDto, ShopNotificationSettingsInput } from '../types'

/** API_CONTRACT_CYCLE24.md §483. Channel connection itself goes through the shared ezbook `notification-channels` API. */
export const shopNotificationsApi = {
  get: (shopId: string) => api.get<ShopNotificationSettingsDto>(`/shops/${shopId}/notification-settings`).then((r) => r.data),
  put: (shopId: string, data: ShopNotificationSettingsInput) =>
    api.put<ShopNotificationSettingsDto>(`/shops/${shopId}/notification-settings`, data).then((r) => r.data),
}
