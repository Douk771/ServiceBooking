import { api } from '@/api/client'
import type { CreateOrderInput, CreateOrderResponse, QuoteDto, QuoteInput, StorefrontDto } from '../types'

/** API_CONTRACT_CYCLE23.md §411–§413. Public; a signed-in caller's token is attached by the shared client. */
export const storefrontApi = {
  get: (slug: string) => api.get<StorefrontDto>(`/storefront/${slug}`).then((r) => r.data),
  quote: (slug: string, data: QuoteInput) => api.post<QuoteDto>(`/storefront/${slug}/quote`, data).then((r) => r.data),
  /** 201 for a new order, 200 for an idempotent repeat — both carry the same body. */
  createOrder: (slug: string, data: CreateOrderInput) =>
    api.post<CreateOrderResponse>(`/storefront/${slug}/orders`, data).then((r) => r.data),
}
