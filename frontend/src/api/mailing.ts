import { api } from './client'

export interface MailLog {
  id: string
  subject: string
  message: string
  recipientCount: number
  sentAt: string
}

export const mailingApi = {
  send: (companyId: string, data: { subject: string; message: string }) =>
    api.post<{ recipientCount: number; message: string }>(`/companies/${companyId}/mail`, data).then((r) => r.data),
  history: (companyId: string) => api.get<MailLog[]>(`/companies/${companyId}/mail`).then((r) => r.data),
}
