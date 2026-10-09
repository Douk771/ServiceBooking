import { api } from '@/api/client'
import type { BathScheduleDto, BathsCompanyManageDto, RevisionDto } from '../cabinet/types'

/** Cabinet routes of one «Бани» company that the day, the bookings and the schedule need (API_CONTRACT_CYCLE42.md). */
export const bathsCabinetApi = {
  company: (companyId: string) => api.get<BathsCompanyManageDto>(`/baths/companies/${companyId}`).then((r) => r.data),
  /** `StaysSettings.BookingsRevision`: a cheap poll that tells whether the day, the list or the schedule must be re-read. */
  revision: (companyId: string) => api.get<RevisionDto>(`/baths/companies/${companyId}/revision`).then((r) => r.data),
  schedule: (companyId: string, params: { from?: string; days?: number }) =>
    api.get<BathScheduleDto>(`/baths/companies/${companyId}/schedule`, { params }).then((r) => r.data),
}
