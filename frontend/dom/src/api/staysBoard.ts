import { api } from '@/api/client'
import type {
  HouseBlockDto,
  HouseBlockInput,
  ManualStayBookingInput,
  StaffStayBookingCardDto,
  StaffStayBookingPage,
  StaffStayQuoteInput,
  StayQuoteDto,
  StaysBoardDto,
  StaysScheduleDto,
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

/** Board, blocks, bookings and schedule of the cabinet (API_CONTRACT_CYCLE37.md §37.29–§37.31). */
export const staysBoardApi = {
  board: (companyId: string, params: { from?: string; days?: number; sinceRevision?: number }) =>
    api.get<StaysBoardDto>(`${co(companyId)}/board`, { params }).then((r) => r.data),

  createBlock: (companyId: string, input: HouseBlockInput) => api.post<HouseBlockDto>(`${co(companyId)}/blocks`, input).then((r) => r.data),
  updateBlock: (companyId: string, blockId: string, input: HouseBlockInput) =>
    api.put<HouseBlockDto>(`${co(companyId)}/blocks/${blockId}`, input).then((r) => r.data),
  deleteBlock: (companyId: string, blockId: string) => api.delete(`${co(companyId)}/blocks/${blockId}`).then(() => undefined),

  schedule: (companyId: string, params?: { from?: string; days?: number }) =>
    api.get<StaysScheduleDto>(`${co(companyId)}/schedule`, { params }).then((r) => r.data),

  bookings: (companyId: string, q: BookingListQuery) =>
    api
      .get<StaffStayBookingPage>(`${co(companyId)}/bookings`, {
        params: q,
        // `status` is repeatable: status=A&status=B (axios default would send status[]=A).
        paramsSerializer: { indexes: null },
      })
      .then((r) => r.data),
  booking: (companyId: string, bookingId: string) =>
    api.get<StaffStayBookingCardDto>(`${co(companyId)}/bookings/${bookingId}`).then((r) => r.data),
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
}
