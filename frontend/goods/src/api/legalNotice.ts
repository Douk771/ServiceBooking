import { api } from '@/api/client'

/**
 * `GET /api/legal/texts/orderCheckoutNotice` (§398.3). The key is not in ezbook's `LegalTextKey` union (it is a
 * cycle-23 constant kept out of `LegalTextKey.All` until the lawyer's text exists), so goods reads it here
 * instead of widening the shared type. A 404 is a normal state, not an error — see CheckoutLegalNotice.
 */
export interface OrderCheckoutNotice {
  version: string
  contentHtml: string
}

export const legalNoticeApi = {
  orderCheckout: () => api.get<OrderCheckoutNotice>('/legal/texts/orderCheckoutNotice').then((r) => r.data),
}
