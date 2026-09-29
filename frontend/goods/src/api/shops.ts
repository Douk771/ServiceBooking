import { api } from '@/api/client'
import type {
  CreateShopInput,
  CreateShopResponse,
  SellerInfoInput,
  ShopListItemDto,
  ShopManageDto,
  ShopSettingsInput,
  SlugCheckDto,
  CompanyKindsSummaryDto,
} from '../types'

/** API_CONTRACT_CYCLE23.md §408–§409. */
export const shopsApi = {
  kindsSummary: () => api.get<CompanyKindsSummaryDto>('/companies/kinds-summary').then((r) => r.data),
  my: () => api.get<ShopListItemDto[]>('/shops/my').then((r) => r.data),
  create: (data: CreateShopInput) => api.post<CreateShopResponse>('/shops', data).then((r) => r.data),
  slugCheck: (params: { slug?: string; name?: string }) =>
    api.get<SlugCheckDto>('/shops/slug-check', { params }).then((r) => r.data),
  get: (shopId: string) => api.get<ShopManageDto>(`/shops/${shopId}`).then((r) => r.data),
  updateSettings: (shopId: string, data: ShopSettingsInput) =>
    api.put<ShopManageDto>(`/shops/${shopId}/settings`, data).then((r) => r.data),
  updateSeller: (shopId: string, data: SellerInfoInput) =>
    api.put<ShopManageDto>(`/shops/${shopId}/seller`, data).then((r) => r.data),
  updateSlug: (shopId: string, slug: string) =>
    api.put<ShopManageDto>(`/shops/${shopId}/slug`, { slug }).then((r) => r.data),
  /** PNG blob via `api` (the endpoint needs the token) — preview with URL.createObjectURL, download the same blob. */
  qr: (shopId: string) => api.get<Blob>(`/shops/${shopId}/qr`, { responseType: 'blob' }).then((r) => r.data),
}
