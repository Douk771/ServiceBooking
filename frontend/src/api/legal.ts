import { api } from './client'
import type { ConsentStatus, LegalDocument, LegalDocumentMeta, LegalDocumentType } from '../types'

export const legalApi = {
  /** GET /api/legal/documents — public, metadata for both documents (API_CONTRACT.md §1). */
  getDocuments: () => api.get<{ documents: LegalDocumentMeta[] }>('/legal/documents').then((r) => r.data.documents),

  /** GET /api/legal/documents/{type} — public, metadata + HTML text of one document
   *  (API_CONTRACT.md §2). `type` is case-insensitive server-side; we always send lowercase. */
  getDocument: (type: LegalDocumentType) =>
    api.get<LegalDocument>(`/legal/documents/${type.toLowerCase()}`).then((r) => r.data),

  /** GET /api/legal/consent-status — authenticated, in the 451 allow-list (API_CONTRACT.md §3). */
  getConsentStatus: () => api.get<ConsentStatus>('/legal/consent-status').then((r) => r.data),

  /** POST /api/legal/accept — authenticated, in the allow-list, returns a NEW token that must
   *  replace the one in authStore or the very next request gets 451 again (API_CONTRACT.md §4). */
  accept: (privacyVersion: string, termsVersion: string) =>
    api
      .post<{ token: string; acceptedAt: string }>('/legal/accept', { privacyVersion, termsVersion })
      .then((r) => r.data),
}
