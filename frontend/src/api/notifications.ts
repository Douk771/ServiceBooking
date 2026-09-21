import { api } from './client'
import type {
  NotificationSettings,
  NotificationTemplatesResponse,
  NotificationTemplate,
  NotificationLogEntry,
  NotificationLogSummary,
  NotificationType,
  Paged,
} from '../types'

export interface NotificationLogFilters {
  page?: number
  pageSize?: number
  status?: string
  type?: string
  from?: string
  to?: string
}

// API_CONTRACT_CYCLE4.md §28–§32.
export const notificationsApi = {
  getSettings: (companyId: string) =>
    api.get<NotificationSettings>(`/companies/${companyId}/notification-settings`).then((r) => r.data),
  updateSettings: (
    companyId: string,
    data: { enabledTypes: NotificationType[]; reminderLeadMinutes: number; minLeadMinutes: number },
  ) => api.put<NotificationSettings>(`/companies/${companyId}/notification-settings`, data).then((r) => r.data),

  getTemplates: (companyId: string) =>
    api.get<NotificationTemplatesResponse>(`/companies/${companyId}/notification-templates`).then((r) => r.data),
  updateTemplate: (companyId: string, type: NotificationType, body: string) =>
    api
      .put<NotificationTemplate>(`/companies/${companyId}/notification-templates/${type}`, { body })
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
