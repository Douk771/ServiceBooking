import { api } from '@/api/client'
import type {
  ActualQuantityInput,
  EditOrderInput,
  IssueQuoteDto,
  MyOrderSummaryDto,
  OrderBoardDto,
  PublicOrderDto,
  StaffOrderDto,
} from '../types'

/** API_CONTRACT_CYCLE23.md §414–§418. */
export const ordersApi = {
  // buyer
  getPublic: (token: string) => api.get<PublicOrderDto>(`/orders/public/${encodeURIComponent(token)}`).then((r) => r.data),
  cancelPublic: (token: string) =>
    api.post<PublicOrderDto>(`/orders/public/${encodeURIComponent(token)}/cancel`).then((r) => r.data),
  my: () => api.get<MyOrderSummaryDto[]>('/orders/my').then((r) => r.data),

  // staff
  board: (shopId: string, params: { sinceRevision?: number; businessDate?: string } = {}) =>
    api.get<OrderBoardDto>(`/shops/${shopId}/order-board`, { params }).then((r) => r.data),
  get: (shopId: string, orderId: string) =>
    api.get<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}`).then((r) => r.data),
  accept: (shopId: string, orderId: string, expectedVersion: number) =>
    api.post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/accept`, { expectedVersion }).then((r) => r.data),
  reject: (shopId: string, orderId: string, expectedVersion: number, reason?: string) =>
    api.post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/reject`, { expectedVersion, reason }).then((r) => r.data),
  ready: (shopId: string, orderId: string, expectedVersion: number) =>
    api.post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/ready`, { expectedVersion }).then((r) => r.data),
  notPickedUp: (shopId: string, orderId: string, expectedVersion: number) =>
    api.post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/not-picked-up`, { expectedVersion }).then((r) => r.data),
  cancel: (shopId: string, orderId: string, expectedVersion: number, reason?: string) =>
    api.post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/cancel`, { expectedVersion, reason }).then((r) => r.data),
  issueQuote: (shopId: string, orderId: string, actualQuantities: ActualQuantityInput[]) =>
    api.post<IssueQuoteDto>(`/shops/${shopId}/orders/${orderId}/issue-quote`, { actualQuantities }).then((r) => r.data),
  issue: (shopId: string, orderId: string, expectedVersion: number, actualQuantities: ActualQuantityInput[]) =>
    api
      .post<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/issue`, { expectedVersion, actualQuantities })
      .then((r) => r.data),
  edit: (shopId: string, orderId: string, data: EditOrderInput) =>
    api.put<StaffOrderDto>(`/shops/${shopId}/orders/${orderId}/items`, data).then((r) => r.data),
}
