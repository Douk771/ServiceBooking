import { api } from './client'
import type { City } from '../types'

// API_CONTRACT_CYCLE4.md §31.1 — anonymous, used by the city combobox (T4-F2).
export const citiesApi = {
  search: (search: string, take = 20) =>
    api.get<{ items: City[] }>('/cities', { params: { search, take } }).then((r) => r.data.items),
}
