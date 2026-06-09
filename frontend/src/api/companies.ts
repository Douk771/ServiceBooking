import { api } from './client'
import type { Company } from '../types'

export interface MemberDto {
  id: string
  userId: string
  firstName: string
  lastName: string
  email: string
  avatarUrl?: string
  role: string
  bio?: string
}

export interface CreateCompanyPayload {
  name: string
  slug: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking: boolean
}

export interface UpdateCompanyPayload {
  name?: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking?: boolean
}

export const companiesApi = {
  getAll: () => api.get<Company[]>('/companies').then((r) => r.data),
  getBySlug: (slug: string) => api.get<Company>(`/companies/${slug}`).then((r) => r.data),
  getMy: () => api.get<Company[]>('/companies/my').then((r) => r.data),
  create: (data: CreateCompanyPayload) => api.post<Company>('/companies', data).then((r) => r.data),
  update: (id: string, data: UpdateCompanyPayload) => api.put<Company>(`/companies/${id}`, data).then((r) => r.data),
  getMembers: (id: string) => api.get<MemberDto[]>(`/companies/${id}/members`).then((r) => r.data),
  addMember: (id: string, email: string, firstName: string, lastName: string, role: string, bio?: string) =>
    api.post<MemberDto>(`/companies/${id}/members`, { email, firstName, lastName, role, bio }).then((r) => r.data),
  removeMember: (companyId: string, memberId: string) =>
    api.delete(`/companies/${companyId}/members/${memberId}`),
}
