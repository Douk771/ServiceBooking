import { api } from './client'
import type { ChannelDto, ChannelOffer } from '../types'

// API_CONTRACT_CYCLE4.md §20–§27 — owner-scoped WhatsApp channel management.
export const notificationChannelsApi = {
  list: () => api.get<{ channels: ChannelDto[] }>('/notification-channels').then((r) => r.data.channels),
  offer: () => api.get<ChannelOffer>('/notification-channels/offer').then((r) => r.data),
  request: () => api.post<ChannelDto>('/notification-channels').then((r) => r.data),
  get: (id: string) => api.get<ChannelDto>(`/notification-channels/${id}`).then((r) => r.data),
  acceptRisk: (id: string, version: string) =>
    api.post<void>(`/notification-channels/${id}/accept-risk`, { version }).then((r) => r.data),
  connect: (id: string) =>
    api.post<{ state: string; refreshAfterSeconds: number }>(`/notification-channels/${id}/connect`).then((r) => r.data),
  getQr: (id: string) =>
    api
      .get<{ state: string; qrBase64: string | null; refreshAfterSeconds: number; expiresInSeconds: number }>(
        `/notification-channels/${id}/qr`,
      )
      .then((r) => r.data),
  testMessage: (id: string) =>
    api.post<{ delivered: boolean; message: string }>(`/notification-channels/${id}/test-message`).then((r) => r.data),
  disconnect: (id: string) => api.delete(`/notification-channels/${id}`),
  replace: (id: string) =>
    api
      .post<{ newChannelId: string; paidUntil: string; companiesMoved: number }>(`/notification-channels/${id}/replace`)
      .then((r) => r.data),
  assignCompany: (id: string, companyId: string, warningAcknowledged: boolean) =>
    api
      .post<ChannelDto>(`/notification-channels/${id}/companies`, { companyId, warningAcknowledged })
      .then((r) => r.data),
  unassignCompany: (id: string, companyId: string) => api.delete(`/notification-channels/${id}/companies/${companyId}`),
}
