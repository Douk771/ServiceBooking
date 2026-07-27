import { api } from './client'

export interface PlanConfig {
  id: string
  name: string
  pricePerMonth: number
  maxEmployees: number | null
  maxCompanies: number | null
  allowOnlineBooking: boolean
  allowMailing: boolean
  allowAnalytics: boolean
  allowPublicListing: boolean
  allowOnlinePayment: boolean
  description: string | null
  isActive: boolean
  notifyDaysBefore: number
}

export const plansApi = {
  list: () => api.get<PlanConfig[]>('/admin/plans').then(r => r.data),
  create: (data: Partial<PlanConfig>) => api.post<PlanConfig>('/admin/plans', data).then(r => r.data),
  update: (id: string, data: Partial<PlanConfig>) => api.put<PlanConfig>(`/admin/plans/${id}`, data).then(r => r.data),
  deactivate: (id: string) => api.delete(`/admin/plans/${id}`),
}
