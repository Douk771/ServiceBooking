import { api } from '@/api/client'
import type { CreateOrderInput, CreateOrderResponse, PickupSlotsDto, QuoteDto, QuoteInput, StorefrontDto } from '../types'

/** API_CONTRACT_CYCLE23.md §411–§413 + API_CONTRACT_CYCLE24.md §477–§478. Public; a signed-in caller's token is attached by the shared client. */
export const storefrontApi = {
  /** `date` (YYYY-MM-DD) switches the assortment to that pick-up day; an unavailable date answers with today's + `dateNotice`. */
  get: (slug: string, date?: string) =>
    api.get<StorefrontDto>(`/storefront/${slug}`, { params: date ? { date } : undefined }).then((r) => r.data),
  pickupSlots: (slug: string, date: string) =>
    api.get<PickupSlotsDto>(`/storefront/${slug}/pickup-slots`, { params: { date } }).then((r) => r.data),
  quote: (slug: string, data: QuoteInput) => api.post<QuoteDto>(`/storefront/${slug}/quote`, data).then((r) => r.data),
  /** 201 for a new order, 200 for an idempotent repeat — both carry the same body. */
  createOrder: (slug: string, data: CreateOrderInput) =>
    api.post<CreateOrderResponse>(`/storefront/${slug}/orders`, data).then((r) => r.data),
}
