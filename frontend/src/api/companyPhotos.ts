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

  /** API_CONTRACT_CYCLE20.md §434.8 (Т20-07 п. 2) — `reason` is a NEW, optional query param, the only
   *  accepted value is `DepictedPersonRequest`. It's used ONLY when the server checks the caller is
   *  SuperAdmin — the owner's own removal is unaffected either way, so it's fine to just never send it
   *  from the owner's flow rather than have the server ignore it. When set, the server publishes a
   *  `PhotoRemoved` platform notice to the company's billing account after the commit. */
  remove: (companyId: string, photoId: string, reason?: 'DepictedPersonRequest') =>
    api.delete(`/companies/${companyId}/photos/${photoId}`, { params: reason ? { reason } : undefined }),

  /** §128.2 — `photoIds` must be a full permutation of the company's current photos; first = cover. */
  reorder: (companyId: string, photoIds: string[]) =>
    api.put<CompanyPhoto[]>(`/companies/${companyId}/photos/order`, { photoIds }).then((r) => r.data),
}
