import { api } from './client'

export interface CompanyStats {
  totalRevenue: number
  bookingsCount: number
  completedCount: number
  cancelledCount: number
  newClientsCount: number
  masterStats: { masterId: string; masterName: string; bookingsCount: number; revenue: number }[]
  popularServices: { serviceId: string; serviceName: string; count: number }[]
  dailyRevenue: { date: string; revenue: number }[]
}

export const statsApi = {
  getCompanyStats: (companyId: string, from: string, to: string) =>
    api.get<CompanyStats>(`/companies/${companyId}/stats`, { params: { from, to } }).then((r) => r.data),
}
