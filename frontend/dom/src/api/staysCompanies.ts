import { api } from '@/api/client'
import type {
  ArrivalReminderChangeDto,
  ArrivalReminderInput,
  ArrivalReminderPreviewDto,
  ArrivalReminderPreviewInput,
  ArrivalReminderSettingsDto,
  StaysCompanyManageWithServices,
  StaysSettingsWithServices,
  CompanyKindsSummaryDto,
  PaymentDetailsDto,
  ProviderInput,
  StaysCompanyCreateInput,
  StaysCompanyCreatedDto,
  StaysCompanyListItemDto,
  StaysNotificationSettingsDto,
  StaysNotificationSettingsInput,
  StaysSlugCheckDto,
  StaysTrialOutcomeDto,
  StaysTrialStateDto,
} from '../types'

/** Cabinet: the «Дома» company (API_CONTRACT_CYCLE37.md §37.27). 404 = not a member, 403 = member without the permission. */
export const staysCompaniesApi = {
  kindsSummary: () => api.get<CompanyKindsSummaryDto>('/companies/kinds-summary').then((r) => r.data),
  my: () => api.get<StaysCompanyListItemDto[]>('/stays/companies/my').then((r) => r.data),
  create: (input: StaysCompanyCreateInput) => api.post<StaysCompanyCreatedDto>('/stays/companies', input).then((r) => r.data),
  slugCheck: (params: { name?: string; slug?: string; companyId?: string }) =>
    api.get<StaysSlugCheckDto>('/stays/slug-check', { params }).then((r) => r.data),
  trialState: () => api.get<StaysTrialStateDto>('/stays/trial').then((r) => r.data),
  /** A refusal is a 409 with the same body shape (`granted: false`) — callers read it from the error. */
  activateTrial: (termsVersion: string) => api.post<StaysTrialOutcomeDto>('/stays/trial', { termsVersion }).then((r) => r.data),

  get: (companyId: string) => api.get<StaysCompanyManageWithServices>(`/stays/companies/${companyId}`).then((r) => r.data),
  updateSettings: (companyId: string, input: StaysSettingsWithServices) =>
    api.put<StaysCompanyManageWithServices>(`/stays/companies/${companyId}/settings`, input).then((r) => r.data),
  updatePaymentDetails: (companyId: string, input: PaymentDetailsDto) =>
    api.put<StaysCompanyManageWithServices>(`/stays/companies/${companyId}/payment-details`, input).then((r) => r.data),
  updateProvider: (companyId: string, input: ProviderInput) =>
    api.put<StaysCompanyManageWithServices>(`/stays/companies/${companyId}/provider`, input).then((r) => r.data),
  updateSlug: (companyId: string, slug: string) =>
    api.put<StaysCompanyManageWithServices>(`/stays/companies/${companyId}/slug`, { slug }).then((r) => r.data),
  qr: (companyId: string) => api.get<Blob>(`/stays/companies/${companyId}/qr`, { responseType: 'blob' }).then((r) => r.data),

  notificationSettings: (companyId: string) =>
    api.get<StaysNotificationSettingsDto>(`/stays/companies/${companyId}/notification-settings`).then((r) => r.data),
  updateNotificationSettings: (companyId: string, input: StaysNotificationSettingsInput) =>
    api.put<StaysNotificationSettingsDto>(`/stays/companies/${companyId}/notification-settings`, input).then((r) => r.data),

  // ───── arrival reminder (API_CONTRACT_CYCLE39.md §39.33), ManageCompany ─────
  arrivalReminder: (companyId: string) =>
    api.get<ArrivalReminderSettingsDto>(`/stays/companies/${companyId}/arrival-reminder`).then((r) => r.data),
  /** 409 `ReminderConfirmationRequired` carries `markers[]` and `noticeText`: ask, then repeat with `confirmCodeMarkers: true`. */
  saveArrivalReminder: (companyId: string, input: ArrivalReminderInput) =>
    api.put<ArrivalReminderSettingsDto>(`/stays/companies/${companyId}/arrival-reminder`, input).then((r) => r.data),
  previewArrivalReminder: (companyId: string, input: ArrivalReminderPreviewInput) =>
    api.post<ArrivalReminderPreviewDto>(`/stays/companies/${companyId}/arrival-reminder/preview`, input).then((r) => r.data),
  arrivalReminderHistory: (companyId: string) =>
    api.get<ArrivalReminderChangeDto[]>(`/stays/companies/${companyId}/arrival-reminder/history`).then((r) => r.data),
}
