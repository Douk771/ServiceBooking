import { api } from '@/api/client'
import type {
  HouseBlockDto,
  HouseBlockInput,
  ManualStayBookingInput,
  StaffServiceQuoteInput,
  StaffServiceSessionCardDto,
  StaffServiceSessionPage,
  StaffAddSessionInput,
  ManualServiceOrderInput,
  ServiceDayDto,
  ServiceQuoteDto,
  ServiceStartsDto,
  StaffStayBookingCardWithServices,
  StaysBoardWithServices,
  StaffStayBookingCardDto,
  StaffStayBookingPage,
  StaffStayQuoteInput,
  StayQuoteDto,
  StaysScheduleWithServices,
  StayBookingStatus,
} from '../types'

const co = (companyId: string) => `/stays/companies/${companyId}`

export interface BookingListQuery {
  status?: StayBookingStatus[]
  houseId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export interface SessionListQuery {
  status?: string
  serviceId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

/** Board, blocks, bookings and schedule of the cabinet (API_CONTRACT_CYCLE37.md §37.29–§37.31). */
export const staysBoardApi = {
  board: (companyId: string, params: { from?: string; days?: number; sinceRevision?: number }) =>
    api.get<StaysBoardWithServices>(`${co(companyId)}/board`, { params }).then((r) => r.data),

  createBlock: (companyId: string, input: HouseBlockInput) => api.post<HouseBlockDto>(`${co(companyId)}/blocks`, input).then((r) => r.data),
  updateBlock: (companyId: string, blockId: string, input: HouseBlockInput) =>
    api.put<HouseBlockDto>(`${co(companyId)}/blocks/${blockId}`, input).then((r) => r.data),
  deleteBlock: (companyId: string, blockId: string) => api.delete(`${co(companyId)}/blocks/${blockId}`).then(() => undefined),

  schedule: (companyId: string, params?: { from?: string; days?: number }) =>
    api.get<StaysScheduleWithServices>(`${co(companyId)}/schedule`, { params }).then((r) => r.data),

  bookings: (companyId: string, q: BookingListQuery) =>
    api
      .get<StaffStayBookingPage>(`${co(companyId)}/bookings`, {
        params: q,
        // `status` is repeatable: status=A&status=B (axios default would send status[]=A).
        paramsSerializer: { indexes: null },
      })
      .then((r) => r.data),
  booking: (companyId: string, bookingId: string) =>
    api.get<StaffStayBookingCardWithServices>(`${co(companyId)}/bookings/${bookingId}`).then((r) => r.data),
  confirmPayment: (companyId: string, bookingId: string, expectedVersion: number) =>
    api.post<StaffStayBookingCardDto>(`${co(companyId)}/bookings/${bookingId}/confirm-payment`, { expectedVersion }).then((r) => r.data),
  rejectPayment: (companyId: string, bookingId: string, expectedVersion: number, reason: string) =>
    api.post<StaffStayBookingCardDto>(`${co(companyId)}/bookings/${bookingId}/reject-payment`, { expectedVersion, reason }).then((r) => r.data),
  cancel: (companyId: string, bookingId: string, expectedVersion: number, reason: string) =>
    api.post<StaffStayBookingCardDto>(`${co(companyId)}/bookings/${bookingId}/cancel`, { expectedVersion, reason }).then((r) => r.data),
  proofBlob: (companyId: string, bookingId: string, proofId: string) =>
    api.get<Blob>(`${co(companyId)}/bookings/${bookingId}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),

  manualQuote: (companyId: string, input: StaffStayQuoteInput) =>
    api.post<StayQuoteDto>(`${co(companyId)}/bookings/quote`, input).then((r) => r.data),
  createManual: (companyId: string, input: ManualStayBookingInput) =>
    api.post<StaffStayBookingCardDto>(`${co(companyId)}/bookings`, input).then((r) => r.data),

  // ───── services: «День услуг», sessions of the cabinet (API_CONTRACT_CYCLE39.md §39.29, §39.30) ─────
  serviceDay: (companyId: string, date: string) => api.get<ServiceDayDto>(`${co(companyId)}/service-day`, { params: { date } }).then((r) => r.data),
  sessions: (companyId: string, q: SessionListQuery) =>
    api.get<StaffServiceSessionPage>(`${co(companyId)}/service-sessions`, { params: q }).then((r) => r.data),
  session: (companyId: string, sessionId: string) =>
    api.get<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}`).then((r) => r.data),
  confirmSessionPayment: (companyId: string, sessionId: string, expectedVersion: number) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/confirm-payment`, { expectedVersion }).then((r) => r.data),
  rejectSessionPayment: (companyId: string, sessionId: string, expectedVersion: number, reason: string) =>
    api
      .post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/reject-payment`, { expectedVersion, reason })
      .then((r) => r.data),
  cancelSession: (companyId: string, sessionId: string, expectedVersion: number, reason: string) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions/${sessionId}/cancel`, { expectedVersion, reason }).then((r) => r.data),
  sessionProofBlob: (companyId: string, sessionId: string, proofId: string) =>
    api.get<Blob>(`${co(companyId)}/service-sessions/${sessionId}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),
  staffStarts: (companyId: string, serviceId: string, params: { date: string; bookingId?: string }) =>
    api.get<ServiceStartsDto>(`${co(companyId)}/services/${serviceId}/starts`, { params }).then((r) => r.data),
  sessionQuote: (companyId: string, input: StaffServiceQuoteInput) =>
    api.post<ServiceQuoteDto>(`${co(companyId)}/service-sessions/quote`, input).then((r) => r.data),
  addSessionToBooking: (companyId: string, bookingId: string, input: StaffAddSessionInput) =>
    api.post<StaffStayBookingCardWithServices>(`${co(companyId)}/bookings/${bookingId}/sessions`, input).then((r) => r.data),
  createManualOrder: (companyId: string, input: ManualServiceOrderInput) =>
    api.post<StaffServiceSessionCardDto>(`${co(companyId)}/service-sessions`, input).then((r) => r.data),
}
