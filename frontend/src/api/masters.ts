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
}

export const mastersApi = {
  // API_CONTRACT.md §11.2 (BREAKING) — array replaced by the Paged<T> envelope, sorted by last visit
  // date DESC on the server.
  getClients: (companyId: string, page = 1, pageSize = 20) =>
    api.get<Paged<MasterClient>>('/masters/clients', { params: { companyId, page, pageSize } }).then((r) => r.data),
  addNote: (data: { companyId: string; clientId?: string; guestPhone?: string; note: string; bookingId?: string }) =>
    api.post<ClientNote>('/masters/clients/notes', data).then((r) => r.data),
  deleteNote: (id: string) => api.delete(`/masters/clients/notes/${id}`),
}
