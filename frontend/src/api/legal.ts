import { api } from './client'
import type { ConsentStatus, LegalDocument, LegalDocumentMeta, LegalDocumentType, LegalManifest, LegalText, LegalTextKey } from '../types'

export const legalApi = {
  /** GET /api/legal/documents — public. §39.1 (BREAKING): an object with `documents`+`uiTexts`, not a
   *  bare array. Callers that only need the five document entries can keep using `getDocuments()`. */
  getManifest: () => api.get<LegalManifest>('/legal/documents').then((r) => r.data),
  getDocuments: (): Promise<LegalDocumentMeta[]> => legalApi.getManifest().then((m) => m.documents),

  /** GET /api/legal/documents/{type} — public, metadata + HTML text of one document (§39.2).
   *  `type` is case-insensitive server-side; we always send lowercase. */
  getDocument: (type: LegalDocumentType) =>
    api.get<LegalDocument>(`/legal/documents/${type.toLowerCase()}`).then((r) => r.data),

  /** GET /api/legal/texts/{key} — public, new in cycle 5 (§39.3). Microcopy shown inline in forms
   *  (booking notice, template ad warning, unsubscribe page, photo/health consent, guardian
   *  confirmation) — same shape as a document, minus `changeKind`/`gate`. */
  getText: (key: LegalTextKey) => api.get<LegalText>(`/legal/texts/${key}`).then((r) => r.data),

  /** GET /api/legal/consent-status — authenticated, in the 451 allow-list (§39.4, BREAKING):
   *  `documents[]` now only lists gated (`gate !== "None"`) types, plus `ownerActionBlocked`. */
  getConsentStatus: () => api.get<ConsentStatus>('/legal/consent-status').then((r) => r.data),

  /** POST /api/legal/accept — authenticated, in the allow-list (§39.5, BREAKING): the body is now a
   *  list of `{type, version}` pairs instead of two fixed fields, so a document can be added later
   *  without breaking the shape. Returns a NEW token that must replace the one in authStore, or the
   *  very next request gets 451 again. */
  accept: (accept: { type: LegalDocumentType; version: string }[]) =>
    api.post<{ token: string; acceptedAt: string }>('/legal/accept', { accept }).then((r) => r.data),
}
