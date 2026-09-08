import { api } from './client'

export type PhotoRetention = 'SixMonths' | 'TwelveMonths' | 'Forever'

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
  /** Photo storage quota in MB for client-note photos (US-24); null = unlimited. */
  photoQuotaMb: number | null
  /** How long client-note photos are kept before the cleanup task removes them (US-21, US-24). */
  photoRetention: PhotoRetention
}

export const plansApi = {
  list: () => api.get<PlanConfig[]>('/admin/plans').then((r) => r.data),
  create: (data: Partial<PlanConfig>) => api.post<PlanConfig>('/admin/plans', data).then((r) => r.data),
  update: (id: string, data: Partial<PlanConfig>) =>
    api.put<PlanConfig>(`/admin/plans/${id}`, data).then((r) => r.data),
  deactivate: (id: string) => api.delete(`/admin/plans/${id}`),
}
