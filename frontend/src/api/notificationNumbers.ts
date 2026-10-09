import { api } from './client'
import type { components } from '../types/api-cycle40.generated'

// API_CONTRACT_CYCLE40.md §40.20–§40.39, contracts/cycle40/openapi.yaml. Типы — из генерата (`npm run types:api:cycle40`).
type Schemas = components['schemas']

export type NumbersOverviewDto = Schemas['NumbersOverviewDto']
export type TransportNumbersDto = Schemas['TransportNumbersDto']
export type NumberChannelDto = Schemas['ChannelDto']
export type CreateChannelRequestDto = Schemas['CreateChannelRequestDto']
export type AdminChannelDto = Schemas['AdminChannelDto']
export type AdminChannelListDto = Schemas['AdminChannelListDto']
export type AdminChannelSummaryDto = Schemas['AdminChannelSummaryDto']
export type AdminChannelCardDto = Schemas['AdminChannelCardDto']
export type ConfirmChannelPaymentInput = Schemas['ConfirmChannelPaymentInput']
export type PaymentFilter = Schemas['PaymentFilter']

/** Ключ запроса блока «Номера»; после любого действия — invalidateQueries (§40.15.1). */
export const NUMBERS_OVERVIEW_QUERY_KEY = ['notification-numbers'] as const

// Владелец: общий блок «Номера» (GET overview — новый) и шаги мастера «Оплата»/«Условия» (POST — форма изменена).
// Остальные действия с номером (connect, qr, test-message, replace, delete) — прежние, см. notificationChannels.ts.
export const notificationNumbersApi = {
  overview: () => api.get<NumbersOverviewDto>('/notification-channels/overview').then((r) => r.data),
  /** `paymentRequest: true` — шаг «Оплата»; `false` — шаг «Условия» (без заявки на оплату, §40.7.2). */
  request: (payload: CreateChannelRequestDto) =>
    api.post<NumberChannelDto>('/notification-channels', payload).then((r) => r.data),
}

// SuperAdmin: таблица, карточка и «Подтвердить оплату» (§40.13). suspend/resume — прежние, см. platformSettings.ts.
export const adminNumbersApi = {
  list: (params: {
    state?: Schemas['ChannelState']
    paymentState?: Schemas['ChannelPaymentStatus']
    transport?: Schemas['NotificationTransport']
    displayStatus?: Schemas['ChannelDisplayStatus']
    payment?: PaymentFilter
    includeReplaced?: boolean
    page?: number
    pageSize?: number
  }) => api.get<AdminChannelListDto>('/admin/notification-channels', { params }).then((r) => r.data),
  summary: () => api.get<AdminChannelSummaryDto>('/admin/notification-channels/summary').then((r) => r.data),
  card: (id: string) => api.get<AdminChannelCardDto>(`/admin/notification-channels/${id}`).then((r) => r.data),
  /** 400/409 приходят голой строкой text/plain (русский текст для показа). */
  confirmPayment: (id: string, input: ConfirmChannelPaymentInput) =>
    api.post<AdminChannelCardDto>(`/admin/notification-channels/${id}/confirm-payment`, input).then((r) => r.data),
}
