import { api } from '@/api/client'
import type {
  CompanyKindsSummaryDto,
  PaymentDetailsDto,
  ProviderInput,
  StaysCompanyCreateInput,
  StaysCompanyCreatedDto,
  StaysCompanyListItemDto,
  StaysCompanyManageDto,
  StaysNotificationSettingsDto,
  StaysNotificationSettingsInput,
  StaysSettingsDto,
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

  get: (companyId: string) => api.get<StaysCompanyManageDto>(`/stays/companies/${companyId}`).then((r) => r.data),
  updateSettings: (companyId: string, input: StaysSettingsDto) =>
    api.put<StaysCompanyManageDto>(`/stays/companies/${companyId}/settings`, input).then((r) => r.data),
  updatePaymentDetails: (companyId: string, input: PaymentDetailsDto) =>
    api.put<StaysCompanyManageDto>(`/stays/companies/${companyId}/payment-details`, input).then((r) => r.data),
  updateProvider: (companyId: string, input: ProviderInput) =>
    api.put<StaysCompanyManageDto>(`/stays/companies/${companyId}/provider`, input).then((r) => r.data),
  updateSlug: (companyId: string, slug: string) =>
    api.put<StaysCompanyManageDto>(`/stays/companies/${companyId}/slug`, { slug }).then((r) => r.data),
  qr: (companyId: string) => api.get<Blob>(`/stays/companies/${companyId}/qr`, { responseType: 'blob' }).then((r) => r.data),

  notificationSettings: (companyId: string) =>
    api.get<StaysNotificationSettingsDto>(`/stays/companies/${companyId}/notification-settings`).then((r) => r.data),
  updateNotificationSettings: (companyId: string, input: StaysNotificationSettingsInput) =>
    api.put<StaysNotificationSettingsDto>(`/stays/companies/${companyId}/notification-settings`, input).then((r) => r.data),
}
