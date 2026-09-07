import { api } from './client'
import type { ClientNotePhoto } from './masters'

/**
 * Photos attached to a `ClientNote` (US-17…US-20). Private storage — `url`/`thumbnailUrl` require the
 * `Authorization` header, which a plain `<img src>` never sends, so every read goes through axios as a
 * blob (see `hooks/useAuthedImage.ts`, `ARCHITECTURE.md` §12.2).
 */
export const clientNotesApi = {
  uploadPhoto: (noteId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<ClientNotePhoto>(`/client-notes/${noteId}/photos`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then(r => r.data)
  },
  deletePhoto: (photoId: string) => api.delete(`/client-notes/photos/${photoId}`),
  /** `variant` selects the full-size image (opened only in the viewer modal) or the thumbnail. */
  getPhotoBlob: (photoId: string, variant: 'full' | 'thumb') =>
    api
      .get<Blob>(`/client-notes/photos/${photoId}${variant === 'thumb' ? '/thumb' : ''}`, { responseType: 'blob' })
      .then(r => r.data),
}
