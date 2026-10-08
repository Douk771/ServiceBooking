import { api } from '@/api/client'
import type { PublicStayBookingDto, PushSubscriptionInput, StayMyBookingDto } from '../types'

/** Guest side, addressed by the booking token (a capability URL, API_CONTRACT_CYCLE37.md §37.26). Unknown token → 404, empty body. */
const base = (token: string) => `/stays/bookings/public/${encodeURIComponent(token)}`

export const guestBookingsApi = {
  get: (token: string) => api.get<PublicStayBookingDto>(base(token)).then((r) => r.data),

  /** multipart `file`; the first file moves `Held → AwaitingPaymentCheck`. */
  uploadProof: (token: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<PublicStayBookingDto>(`${base(token)}/payment-proofs`, form, {
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

  cancel: (token: string) => api.post<PublicStayBookingDto>(`${base(token)}/cancel`, {}).then((r) => r.data),

  pushSubscribe: (token: string, input: PushSubscriptionInput) => api.post(`${base(token)}/push-subscription`, input).then(() => undefined),
  pushUnsubscribe: (token: string, endpoint: string) => api.post(`${base(token)}/push-subscription/remove`, { endpoint }).then(() => undefined),

  /** Signed in only (P1, US-37-22). */
  my: () => api.get<StayMyBookingDto[]>('/stays/bookings/my').then((r) => r.data),
}
