import { api } from '@/api/client'
import type { DailyMenuCalendarDto, DailyMenuDto, ProductDto } from '../types'

/** API_CONTRACT_CYCLE24.md §482.1 (category weekdays, P1) and §482.3 (menu for a date). */
export const menuApi = {
  calendar: (shopId: string) => api.get<DailyMenuCalendarDto>(`/shops/${shopId}/daily-menus`).then((r) => r.data),
  get: (shopId: string, date: string) => api.get<DailyMenuDto>(`/shops/${shopId}/daily-menus/${date}`).then((r) => r.data),
  put: (shopId: string, date: string, productIds: string[]) =>
    api.put<DailyMenuDto>(`/shops/${shopId}/daily-menus/${date}`, { productIds }).then((r) => r.data),
  remove: (shopId: string, date: string) => api.delete<void>(`/shops/${shopId}/daily-menus/${date}`),
  copy: (shopId: string, date: string, sourceDate: string) =>
    api.post<DailyMenuDto>(`/shops/${shopId}/daily-menus/${date}/copy`, { sourceDate }).then((r) => r.data),
  setCategoryWeekdays: (shopId: string, categoryId: string, weekdays: string[]) =>
    api.put<ProductDto[]>(`/shops/${shopId}/categories/${categoryId}/weekdays`, { weekdays }).then((r) => r.data),
}
