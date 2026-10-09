import { api } from '@/api/client'

/**
 * `GET /api/legal/texts/OrderCheckoutNotice` (§398.3). The key is not in ezbook's `LegalTextKey` union (it is a
 * cycle-23 constant kept out of `LegalTextKey.All` until the lawyer's text exists), so goods reads it here
 * instead of widening the shared type. A 404 is a normal state, not an error — see CheckoutLegalNotice.
 */
export interface OrderCheckoutNotice {
  version: string
  contentHtml: string
}

export const legalNoticeApi = {
  orderCheckout: () => api.get<OrderCheckoutNotice>('/legal/texts/OrderCheckoutNotice').then((r) => r.data),
}

/** Cycle 24 (API_CONTRACT_CYCLE24.md §478.3–§478.4): both keys are kept out of `LegalTextKey.All` until the lawyer's text exists; 404 = «not written». */
export const orderLegalTextsApi = {
  preorderNotice: () => api.get<OrderCheckoutNotice>('/legal/texts/OrderPreorderNotice').then((r) => r.data),
}
