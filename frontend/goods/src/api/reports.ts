import { api } from '@/api/client'
import type { OrderHistoryPageDto, OrderHistoryQuery, PickListDto, ReportPeriodPreset, ShopSummaryDto, SummaryTopSort } from '../types'

/** API_CONTRACT_CYCLE25.md §526–§528. History is a POST so a phone number never reaches the URL or the logs. */
export const reportsApi = {
  history: (shopId: string, query: OrderHistoryQuery) =>
    api.post<OrderHistoryPageDto>(`/shops/${shopId}/order-history`, query).then((r) => r.data),
  summary: (shopId: string, params: { period?: ReportPeriodPreset; from?: string; to?: string; top?: SummaryTopSort; compare?: boolean }) =>
    api.get<ShopSummaryDto>(`/shops/${shopId}/summary`, { params }).then((r) => r.data),
  pickList: (shopId: string, params: { date?: string; from?: string; to?: string; includeNew?: boolean }) =>
    api.get<PickListDto>(`/shops/${shopId}/picklist`, { params }).then((r) => r.data),
}
