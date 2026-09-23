import { api } from './client'
import type { CompanyPhoto } from '../types'

/**
 * API_CONTRACT_CYCLE10.md §125–§128 — company gallery photos. Owner of the company or SuperAdmin
 * only (write side); the list is public/anonymous.
 */
export const companyPhotosApi = {
  list: (companyId: string) => api.get<CompanyPhoto[]>(`/companies/${companyId}/photos`).then((r) => r.data),

  /**
   * §127 — 201 for a genuinely new photo, 200 when the exact same file (by processed-byte hash) was
   * already uploaded — either way the caller gets back a real `CompanyPhoto`, so callers don't need
   * to branch on status code.
   */
  upload: (companyId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<CompanyPhoto>(`/companies/${companyId}/photos`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data)
  },

  remove: (companyId: string, photoId: string) => api.delete(`/companies/${companyId}/photos/${photoId}`),

  /** §128.2 — `photoIds` must be a full permutation of the company's current photos; first = cover. */
  reorder: (companyId: string, photoIds: string[]) =>
    api.put<CompanyPhoto[]>(`/companies/${companyId}/photos/order`, { photoIds }).then((r) => r.data),
}
