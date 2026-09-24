import { api } from './client'
import type { Paged } from '../types'

export interface ClientNotePhoto {
  id: string
  /** Path to the private serving endpoint — NOT a value for <img src>, see AuthedImage. */
  url: string
  /** Path to the private thumbnail endpoint — NOT a value for <img src>, see AuthedImage. */
  thumbnailUrl: string
  width: number
  height: number
  sizeBytes: number
  createdAt: string
  /** Null when the uploader's account was deleted (UploadedByUserId is SetNull) — a real scenario
   *  after the phone-normalization migration, which deletes accounts on collision (US-26). */
  uploadedByName: string | null
  canDelete: boolean
}

export interface ClientNote {
  id: string
  note: string
  createdAt: string
  authorId: string
  authorName: string
  bookingId: string | null
  bookingDate: string | null
  bookingServiceName: string | null
  canDelete: boolean
  photos: ClientNotePhoto[]
}

export interface MasterClient {
  clientId: string | null
  guestPhone: string | null
  name: string
  phone: string | null
  email: string | null
  lastVisitDate: string
  totalVisits: number
  notes: ClientNote[]
  bookingSummaries?: { date: string; serviceName: string; status: string }[]
  /** API_CONTRACT_CYCLE14.md §170.2 — `true` → badge; `false`/`null`/`undefined` → render NOTHING
   *  (R15: no colour, no icon, no "not verified" text; not a gate, no sort/filter by this field). */
  phoneVerified?: boolean | null
}

export const mastersApi = {
  // API_CONTRACT.md §11.2 (BREAKING) — array replaced by the Paged<T> envelope, sorted by last visit
  // date DESC on the server.
  //
  // `search` (cycle C fix, T-F5 follow-up; API_CONTRACT.md §18.1) filters by name and phone on the
  // server, applied before pagination, matching the `?search=` convention already used by
  // `/api/admin/users` and `/api/admin/companies` (§11.2).
  getClients: (companyId: string, page = 1, pageSize = 20, search = '') =>
    api
      .get<Paged<MasterClient>>('/masters/clients', {
        params: { companyId, page, pageSize, search: search.trim() || undefined },
      })
      .then((r) => r.data),
  addNote: (data: { companyId: string; clientId?: string; guestPhone?: string; note: string; bookingId?: string }) =>
    api.post<ClientNote>('/masters/clients/notes', data).then((r) => r.data),
  deleteNote: (id: string) => api.delete(`/masters/clients/notes/${id}`),
}
