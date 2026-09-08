import { api } from './client'
import type { Service } from '../types'

export interface CreateServicePayload {
  companyId: string
  name: string
  description?: string
  durationMinutes: number
  price: number
  imageUrl?: string
}

export const servicesApi = {
  getByCompany: (companyId: string) => api.get<Service[]>('/services', { params: { companyId } }).then((r) => r.data),
  create: (data: CreateServicePayload) => api.post<Service>('/services', data).then((r) => r.data),
  update: (id: string, data: CreateServicePayload) => api.put<Service>(`/services/${id}`, data).then((r) => r.data),
  delete: (id: string) => api.delete(`/services/${id}`),
  uploadImage: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<Service>(`/services/${id}/image`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data)
  },
}
