import { api } from './client'
import type {
  NotificationSettings,
  NotificationTemplatesResponse,
  NotificationTemplate,
  NotificationLogEntry,
  NotificationLogSummary,
  NotificationType,
  NotificationDeliveryMode,
  NotificationTransport,
  Paged,
} from '../types'

export interface NotificationLogFilters {
  page?: number
  pageSize?: number
  status?: string
  type?: string
  from?: string
  to?: string
  /** API_CONTRACT_CYCLE9.md §114.3 — filters the delivery log to one transport. */
  transport?: NotificationTransport
}

// API_CONTRACT_CYCLE4.md §28–§32.
export const notificationsApi = {
  getSettings: (companyId: string) =>
    api.get<NotificationSettings>(`/companies/${companyId}/notification-settings`).then((r) => r.data),
  updateSettings: (
    companyId: string,
    data: {
      enabledTypes: NotificationType[]
      reminderLeadMinutes: number
      minLeadMinutes: number
      /** API_CONTRACT_CYCLE9.md §114.4 — optional; omitting a field means "don't change it". */
      deliveryMode?: NotificationDeliveryMode
      priorityTransport?: NotificationTransport
    },
  ) => api.put<NotificationSettings>(`/companies/${companyId}/notification-settings`, data).then((r) => r.data),

  getTemplates: (companyId: string) =>
    api.get<NotificationTemplatesResponse>(`/companies/${companyId}/notification-templates`).then((r) => r.data),
  /** API_CONTRACT_CYCLE5.md §47.2 (BREAKING № 6) — every save now carries an `acknowledgement`; a
   *  save without it, or with `accepted: false`, is a 400. `confirmedDespiteMarkers` is only read by
   *  the server when the ad-marker check actually hit (§47.2 second 400 case). */
  updateTemplate: (
    companyId: string,
    type: NotificationType,
    body: string,
    acknowledgement: { warningVersion: string; accepted: true; confirmedDespiteMarkers: boolean },
  ) =>
    api
      .put<NotificationTemplate>(`/companies/${companyId}/notification-templates/${type}`, { body, acknowledgement })
      .then((r) => r.data),
  previewTemplate: (companyId: string, type: NotificationType, body: string) =>
    api
      .post<{ rendered: string }>(`/companies/${companyId}/notification-templates/${type}/preview`, { body })
      .then((r) => r.data),

  getLog: (companyId: string, filters: NotificationLogFilters) =>
    api
      .get<Paged<NotificationLogEntry>>(`/companies/${companyId}/notifications`, { params: filters })
      .then((r) => r.data),
  getLogSummary: (companyId: string, days = 30) =>
    api
      .get<NotificationLogSummary>(`/companies/${companyId}/notifications/summary`, { params: { days } })
      .then((r) => r.data),

  getPreferences: () => api.get<{ enabled: boolean }>('/notifications/preferences').then((r) => r.data),
  updatePreferences: (enabled: boolean) => api.put<void>('/notifications/preferences', { enabled }),

  getUnsubscribeInfo: (token: string) =>
    api
      .get<{ phoneMasked: string; alreadyOptedOut: boolean }>(`/notifications/unsubscribe/${token}`)
      .then((r) => r.data),
  unsubscribe: (token: string) => api.post<void>(`/notifications/unsubscribe/${token}`),
}
