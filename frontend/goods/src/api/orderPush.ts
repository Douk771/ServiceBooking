import { api } from '@/api/client'
import type { OrderPushStateDto } from '../types'

/** API_CONTRACT_CYCLE24.md §479.1–§479.2 — the customer's browser push for ONE order (by token, no account). */
export const orderPushApi = {
  subscribe: (token: string, input: { endpoint: string; keys: { p256dh: string; auth: string }; deviceLabel?: string }) =>
    api.post<OrderPushStateDto>(`/orders/public/${encodeURIComponent(token)}/push-subscription`, input).then((r) => r.data),
  /** POST, not DELETE-with-body (proxies drop it); 204 even when there was nothing to remove. */
  unsubscribe: (token: string, endpoint: string) =>
    api.post<void>(`/orders/public/${encodeURIComponent(token)}/push-subscription/remove`, { endpoint }),
}
