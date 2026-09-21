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
  /** @deprecated Cycle 5: aggregate seat cap across the whole billing account, kept only for display. */
  maxEmployees: number | null
  maxCompanies: number | null
  /** Cycle 5 additions (ARCHITECTURE_CYCLE5.md §56, ProfilePlanDtoV5Additions). */
  totalMonthlyPrice?: number | null
  currency?: string
  companiesUsed?: number
  employeesUsed?: number
  expiresInDays?: number | null
  isExpiringSoon?: boolean
  optionCount?: number
}

export interface ProfileDto {
  id: string
  phone: string
  email?: string | null
  firstName: string
  lastName: string
  avatarUrl?: string
  roles: string[]
  /** Only present for CompanyOwner — subscriptions are bound to the owner account. */
  plan: ProfilePlanDto | null
}

export interface UpdateProfilePayload {
  firstName: string
  lastName: string
}

export const profileApi = {
  get: () => api.get<ProfileDto>('/profile').then((r) => r.data),
  update: (data: UpdateProfilePayload) => api.put<ProfileDto>('/profile', data).then((r) => r.data),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post('/profile/change-password', { currentPassword, newPassword }),
  changePhone: (currentPassword: string, newPhone: string) =>
    api.post<ProfileDto>('/profile/change-phone', { currentPassword, newPhone }).then((r) => r.data),
  uploadAvatar: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api
      .post<ProfileDto>('/profile/avatar', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data)
  },
  /** GET /api/profile/export — US-38. Fetched as a blob (token still needed, hence going through the
   *  same axios instance rather than a plain <a href>) and turned into a download via createObjectURL
   *  by the caller (API_CONTRACT.md §8). */
  exportData: () => api.get('/profile/export', { responseType: 'blob' }).then((r) => r.data as Blob),
  /** POST /api/profile/delete-account — US-39. 204 on success. */
  deleteAccount: (currentPassword: string) => api.post('/profile/delete-account', { currentPassword }),
}
