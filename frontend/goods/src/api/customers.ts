import { api } from '@/api/client'
import type { ShopCustomerCardDto, ShopCustomerNoteStateDto } from '../types'

/** API_CONTRACT_CYCLE25.md §529–§530. `customerRef` is the id of any order of this buyer in this shop — never a phone. */
export const customersApi = {
  card: (shopId: string, customerRef: string, page = 1) =>
    api.get<ShopCustomerCardDto>(`/shops/${shopId}/customers/${customerRef}`, { params: { page } }).then((r) => r.data),
  getNote: (shopId: string, customerRef: string) =>
    api.get<ShopCustomerNoteStateDto>(`/shops/${shopId}/customers/${customerRef}/note`).then((r) => r.data),
  putNote: (shopId: string, customerRef: string, text: string | null) =>
    api.put<ShopCustomerNoteStateDto>(`/shops/${shopId}/customers/${customerRef}/note`, { text }).then((r) => r.data),
}
