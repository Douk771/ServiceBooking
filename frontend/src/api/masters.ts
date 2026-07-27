import { api } from './client'

export interface MasterClient {
  clientId: string | null
  guestPhone: string | null
  name: string
  phone: string | null
  email: string | null
  lastVisitDate: string
  totalVisits: number
  notes: string[]
  bookingSummaries?: { date: string; serviceName: string; status: string }[]
}

export const mastersApi = {
  getClients: (companyId: string) =>
    api.get<MasterClient[]>('/masters/clients', { params: { companyId } }).then(r => r.data),
  addNote: (data: { companyId: string; clientId?: string; guestPhone?: string; note: string }) =>
    api.post('/masters/clients/notes', data),
  deleteNote: (id: string) =>
    api.delete(`/masters/clients/notes/${id}`),
}
