import { api } from './client'
import type { Service } from '../types'

export const servicesApi = {
  getByCompany: (companyId: string) =>
    api.get<Service[]>('/services', { params: { companyId } }).then((r) => r.data),
}
