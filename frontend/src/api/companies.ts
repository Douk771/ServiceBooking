import { api } from './client'
import type { Company } from '../types'

export const companiesApi = {
  getAll: () => api.get<Company[]>('/companies').then((r) => r.data),
  getBySlug: (slug: string) => api.get<Company>(`/companies/${slug}`).then((r) => r.data),
}
