import { api } from './client'
import type { Company } from '../types'

export interface MasterPublicDto {
  userId: string
  firstName: string
  lastName: string
  avatarUrl?: string
  bio?: string
}

export interface MemberDto {
  id: string
  userId: string
  firstName: string
  lastName: string
  phone: string
  email?: string | null
  avatarUrl?: string
  role: string
  bio?: string
  serviceIds: string[]
  commissionPercent: number
}

export interface CreateCompanyPayload {
  name: string
  slug: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking: boolean
  showInPublicListing?: boolean
}

export interface UpdateCompanyPayload {
  name?: string
  description?: string
  address?: string
  phone?: string
  email?: string
  allowSelfBooking?: boolean
  requirePrepayment?: boolean
  showInPublicListing?: boolean
}

export const companiesApi = {
  getAll: () => api.get<Company[]>('/companies').then((r) => r.data),
  getBySlug: (slug: string) => api.get<Company>(`/companies/${slug}`).then((r) => r.data),
  getMy: () => api.get<Company[]>('/companies/my').then((r) => r.data),
  getMemberOf: () => api.get<Company[]>('/companies/member').then((r) => r.data),
  create: (data: CreateCompanyPayload) => api.post<Company>('/companies', data).then((r) => r.data),
  update: (id: string, data: UpdateCompanyPayload) => api.put<Company>(`/companies/${id}`, data).then((r) => r.data),
  getMasters: (id: string, serviceId?: string) =>
    api.get<MasterPublicDto[]>(`/companies/${id}/masters`, { params: serviceId ? { serviceId } : {} }).then((r) => r.data),
  getMembers: (id: string) => api.get<MemberDto[]>(`/companies/${id}/members`).then((r) => r.data),
  addMember: (id: string, phone: string, firstName: string, lastName: string, role: string, bio?: string, email?: string) =>
    api.post<MemberDto>(`/companies/${id}/members`, { phone, firstName, lastName, role, bio, email }).then((r) => r.data),
  removeMember: (companyId: string, memberId: string) =>
    api.delete(`/companies/${companyId}/members/${memberId}`),
  updateMemberServices: (companyId: string, memberId: string, serviceIds: string[]) =>
    api.put(`/companies/${companyId}/members/${memberId}/services`, serviceIds),
  updateMemberCommission: (companyId: string, memberId: string, commissionPercent: number) =>
    api.put(`/companies/${companyId}/members/${memberId}/commission`, { commissionPercent }),
  uploadLogo: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<Company>(`/companies/${id}/logo`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then((r) => r.data)
  },
}
