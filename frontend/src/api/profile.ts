import { api } from './client'

export interface ProfilePlanDto {
  planName: string
  pricePerMonth: number
  isActive: boolean
  paidUntil: string | null
  isExpired: boolean
  allowOnlineBooking: boolean
  allowMailing: boolean
  allowAnalytics: boolean
  maxEmployees: number | null
  maxCompanies: number | null
}

export interface ProfileDto {
  id: string
  phone: string
  email?: string | null
  firstName: string
  lastName: string
  avatarUrl?: string
  commissionPercent: number
  roles: string[]
  /** Only present for CompanyOwner — subscriptions are bound to the owner account. */
  plan: ProfilePlanDto | null
}

export interface UpdateProfilePayload {
  firstName: string
  lastName: string
}

export const profileApi = {
  get: () => api.get<ProfileDto>('/profile').then(r => r.data),
  update: (data: UpdateProfilePayload) => api.put<ProfileDto>('/profile', data).then(r => r.data),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post('/profile/change-password', { currentPassword, newPassword }),
}
