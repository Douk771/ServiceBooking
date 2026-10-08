import { api } from '@/api/client'
import type {
  AddSessionInput,
  BookingServicesDto,
    PublicStayBookingWithServices,
  PushSubscriptionInput,
  ServiceQuoteDto,
  ServiceSelectionInput,
  ServiceStartsDto,
  StayMyBookingDto,
} from '../types'

/** Guest side, addressed by the booking token (a capability URL, API_CONTRACT_CYCLE37.md §37.26). Unknown token → 404, empty body. */
const base = (token: string) => `/stays/bookings/public/${encodeURIComponent(token)}`

export const guestBookingsApi = {
  get: (token: string) => api.get<PublicStayBookingWithServices>(base(token)).then((r) => r.data),

  /** multipart `file`; the first file moves `Held → AwaitingPaymentCheck`. */
  uploadProof: (token: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<PublicStayBookingWithServices>(`${base(token)}/payment-proofs`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e) => {
              if (e.total) onProgress(Math.round((e.loaded / e.total) * 100))
            }
          : undefined,
      })
      .then((r) => r.data)
  },

  /** The file of this booking as a blob (token in the URL path, no JWT needed). PDF downloads, images preview. */
  proofBlob: (token: string, proofId: string) =>
    api.get<Blob>(`${base(token)}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),

  cancel: (token: string) => api.post<PublicStayBookingWithServices>(`${base(token)}/cancel`, {}).then((r) => r.data),

  pushSubscribe: (token: string, input: PushSubscriptionInput) => api.post(`${base(token)}/push-subscription`, input).then(() => undefined),
  pushUnsubscribe: (token: string, endpoint: string) => api.post(`${base(token)}/push-subscription/remove`, { endpoint }).then(() => undefined),

  /** Services of the stay (API_CONTRACT_CYCLE39.md §39.24). The prepayment never includes them: pay on the spot. */
  services: (token: string) => api.get<BookingServicesDto>(`${base(token)}/services`).then((r) => r.data),
  serviceStarts: (token: string, serviceId: string, date: string) =>
    api.get<ServiceStartsDto>(`${base(token)}/services/${serviceId}/starts`, { params: { date } }).then((r) => r.data),
  serviceQuote: (token: string, serviceId: string, input: ServiceSelectionInput) =>
    api.post<ServiceQuoteDto>(`${base(token)}/services/${serviceId}/quote`, input).then((r) => r.data),
  addSession: (token: string, input: AddSessionInput) => api.post<PublicStayBookingWithServices>(`${base(token)}/sessions`, input).then((r) => r.data),
  cancelSession: (token: string, sessionId: string) =>
    api.post<PublicStayBookingWithServices>(`${base(token)}/sessions/${sessionId}/cancel`, {}).then((r) => r.data),

  /** Signed in only (P1, US-37-22). */
  my: () => api.get<StayMyBookingDto[]>('/stays/bookings/my').then((r) => r.data),
}
