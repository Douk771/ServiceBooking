import { api } from '@/api/client'
import type { PublicServiceOrderDto, PushSubscriptionInput } from '../types'

/** A separate session by its token, `/s/<token>` (API_CONTRACT_CYCLE39.md §39.23). Unknown token → 404, empty body. */
const base = (token: string) => `/stays/service-orders/public/${encodeURIComponent(token)}`

export const serviceOrdersApi = {
  get: (token: string) => api.get<PublicServiceOrderDto>(base(token)).then((r) => r.data),

  uploadProof: (token: string, file: File, onProgress?: (percent: number) => void) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<PublicServiceOrderDto>(`${base(token)}/payment-proofs`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        onUploadProgress: onProgress
          ? (e) => {
              if (e.total) onProgress(Math.round((e.loaded / e.total) * 100))
            }
          : undefined,
      })
      .then((r) => r.data)
  },
  proofBlob: (token: string, proofId: string) =>
    api.get<Blob>(`${base(token)}/payment-proofs/${proofId}`, { responseType: 'blob' }).then((r) => r.data),
  cancel: (token: string) => api.post<PublicServiceOrderDto>(`${base(token)}/cancel`, {}).then((r) => r.data),
  pushSubscribe: (token: string, input: PushSubscriptionInput) => api.post(`${base(token)}/push-subscription`, input).then(() => undefined),
  pushUnsubscribe: (token: string, endpoint: string) => api.post(`${base(token)}/push-subscription/remove`, { endpoint }).then(() => undefined),
}
