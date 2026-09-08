import { api } from './client'
import type { BookingStatus, Paged } from '../types'

export interface AdminStats {
  totalCompanies: number
  totalUsers: number
  totalBookings: number
  completedBookings: number
  totalRevenue: number
}

export interface AdminUser {
  id: string
  phone: string
  email?: string | null
  firstName: string
  lastName: string
  avatarUrl?: string
  createdAt: string
  roles: string[]
  ownedCompanyCount: number
  planConfigId?: string
  planName: string
  paidUntil?: string
  subscriptionActive: boolean
}

export interface AdminCompany {
  id: string
  name: string
  slug: string
  email?: string
  phone?: string
  isActive: boolean
  allowSelfBooking: boolean
  createdAt: string
  memberCount: number
  bookingCount: number
  ownerUserId: string
  ownerEmail: string
  planConfigId?: string
  planName: string
  paidUntil?: string
  subscriptionActive: boolean
}

export interface SubscriptionChangeLogEntry {
  id: string
  changedAt: string
  changedByEmail: string
  oldPlanName: string
  newPlanName: string
  oldPaidUntil?: string
  newPaidUntil?: string
  oldIsActive: boolean
  newIsActive: boolean
  comment?: string
}

export interface AdminBooking {
  id: string
  companyName: string
  serviceName: string
  masterName: string
  clientName: string
  clientPhone?: string
  date: string
  startTime: string
  endTime: string
  status: BookingStatus
  price: number
}

export interface MasterReport {
  masterId: string
  masterName: string
  commissionPercent: number
  bookingsCount: number
  totalAmount: number
  masterEarnings: number
  companyEarnings: number
}

export const adminApi = {
  getStats: () => api.get<AdminStats>('/admin/stats').then((r) => r.data),
  // API_CONTRACT.md §11.2 (BREAKING) — array replaced by the Paged<T> envelope.
  getUsers: (search?: string, page = 1, pageSize = 20) =>
    api.get<Paged<AdminUser>>('/admin/users', { params: { search, page, pageSize } }).then((r) => r.data),
  updateUserRoles: (id: string, roles: string[]) => api.put(`/admin/users/${id}/roles`, roles),
  getCompanies: (search?: string, page = 1, pageSize = 20) =>
    api.get<Paged<AdminCompany>>('/admin/companies', { params: { search, page, pageSize } }).then((r) => r.data),
  updateSubscription: (
    ownerUserId: string,
    planConfigId: string | null,
    paidUntil: string | null,
    isActive: boolean,
    comment?: string,
  ) => api.put(`/admin/owners/${ownerUserId}/subscription`, { planConfigId, paidUntil, isActive, comment }),
  getSubscriptionHistory: (ownerUserId: string) =>
    api.get<SubscriptionChangeLogEntry[]>(`/admin/owners/${ownerUserId}/subscription-history`).then((r) => r.data),
  updateCompany: (id: string, data: { name: string; isActive: boolean; allowSelfBooking: boolean }) =>
    api.put(`/admin/companies/${id}`, data),
  updateCompanyOwner: (id: string, newOwnerUserId: string) =>
    api.put(`/admin/companies/${id}/owner`, { newOwnerUserId }),
  getBookings: (params: { companyId?: string; from?: string; to?: string; status?: string }) =>
    api.get<AdminBooking[]>('/admin/bookings', { params }).then((r) => r.data),
  getMastersReport: (companyId: string, from: string, to: string) =>
    api.get<MasterReport[]>('/reports/masters', { params: { companyId, from, to } }).then((r) => r.data),
}
