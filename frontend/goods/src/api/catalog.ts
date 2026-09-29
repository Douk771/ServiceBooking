import { api } from '@/api/client'
import type { CategoryDto, CategoryInput, ProductDto, ProductInput } from '../types'

/** API_CONTRACT_CYCLE23.md §410. Owner: everything; staff: only sold-out and stock. */
export const catalogApi = {
  categories: (shopId: string) => api.get<CategoryDto[]>(`/shops/${shopId}/categories`).then((r) => r.data),
  createCategory: (shopId: string, data: CategoryInput) =>
    api.post<CategoryDto>(`/shops/${shopId}/categories`, data).then((r) => r.data),
  updateCategory: (shopId: string, id: string, data: CategoryInput) =>
    api.put<CategoryDto>(`/shops/${shopId}/categories/${id}`, data).then((r) => r.data),
  deleteCategory: (shopId: string, id: string) => api.delete<void>(`/shops/${shopId}/categories/${id}`),
  reorderCategories: (shopId: string, ids: string[]) =>
    api.put<CategoryDto[]>(`/shops/${shopId}/category-order`, { ids }).then((r) => r.data),

  products: (shopId: string, params: { search?: string; categoryId?: string } = {}) =>
    api.get<ProductDto[]>(`/shops/${shopId}/products`, { params }).then((r) => r.data),
  createProduct: (shopId: string, data: ProductInput) =>
    api.post<ProductDto>(`/shops/${shopId}/products`, data).then((r) => r.data),
  updateProduct: (shopId: string, id: string, data: ProductInput) =>
    api.put<ProductDto>(`/shops/${shopId}/products/${id}`, data).then((r) => r.data),
  deleteProduct: (shopId: string, id: string) => api.delete<void>(`/shops/${shopId}/products/${id}`),
  reorderProducts: (shopId: string, categoryId: string | null, productIds: string[]) =>
    api.put<ProductDto[]>(`/shops/${shopId}/product-order`, { categoryId, productIds }).then((r) => r.data),

  uploadImage: (shopId: string, id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<ProductDto>(`/shops/${shopId}/products/${id}/image`, form, { headers: { 'Content-Type': 'multipart/form-data' } })
      .then((r) => r.data)
  },
  deleteImage: (shopId: string, id: string) =>
    api.delete<ProductDto>(`/shops/${shopId}/products/${id}/image`).then((r) => r.data),

  setSoldOut: (shopId: string, id: string, isSoldOut: boolean) =>
    api.put<ProductDto>(`/shops/${shopId}/products/${id}/sold-out`, { isSoldOut }).then((r) => r.data),
  setStock: (shopId: string, id: string, onHand: number | null) =>
    api.put<ProductDto>(`/shops/${shopId}/products/${id}/stock`, { onHand }).then((r) => r.data),
}
