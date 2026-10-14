import { api } from '@/api/client'
import type {
  BathScheduleDto,
  BathsCompanyManageDto,
  BathsSettingsDto,
  NotificationSettingsDto,
  NotificationSettingsInput,
  PaymentDetailsDto,
  ProviderInput,
  RevisionDto,
} from '../cabinet/types'

/** Cabinet routes of one «Бани» company that the day, the bookings and the schedule need (API_CONTRACT_CYCLE42.md). */
export const bathsCabinetApi = {
  company: (companyId: string) => api.get<BathsCompanyManageDto>(`/baths/companies/${companyId}`).then((r) => r.data),
  /** `StaysSettings.BookingsRevision`: a cheap poll that tells whether the day, the list or the schedule must be re-read. */
  revision: (companyId: string) => api.get<RevisionDto>(`/baths/companies/${companyId}/revision`).then((r) => r.data),
  schedule: (companyId: string, params: { from?: string; days?: number }) =>
    api.get<BathScheduleDto>(`/baths/companies/${companyId}/schedule`, { params }).then((r) => r.data),
  /** Full replacement of the booking settings (`ManageCompany`); 400 texts name the field (§42.28). */
  updateSettings: (companyId: string, input: BathsSettingsDto) =>
    api.put<BathsCompanyManageDto>(`/baths/companies/${companyId}/settings`, input).then((r) => r.data),
  updatePaymentDetails: (companyId: string, input: PaymentDetailsDto) =>
    api.put<BathsCompanyManageDto>(`/baths/companies/${companyId}/payment-details`, input).then((r) => r.data),
  updateProvider: (companyId: string, input: ProviderInput) =>
    api.put<BathsCompanyManageDto>(`/baths/companies/${companyId}/provider`, input).then((r) => r.data),
  /** 409 `BathsConflictDto` (`SlugInvalid` / `SlugReserved` / `SlugTaken`). */
  updateSlug: (companyId: string, slug: string) =>
    api.put<BathsCompanyManageDto>(`/baths/companies/${companyId}/slug`, { slug }).then((r) => r.data),
  qr: (companyId: string) => api.get<Blob>(`/baths/companies/${companyId}/qr`, { responseType: 'blob' }).then((r) => r.data),
  notificationSettings: (companyId: string) =>
    api.get<NotificationSettingsDto>(`/baths/companies/${companyId}/notification-settings`).then((r) => r.data),
  /** 409 `MessengerUnavailable` when the guest messenger is switched on without a paid channel. */
  updateNotificationSettings: (companyId: string, input: NotificationSettingsInput) =>
    api.put<NotificationSettingsDto>(`/baths/companies/${companyId}/notification-settings`, input).then((r) => r.data),
}
