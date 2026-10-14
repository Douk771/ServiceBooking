import { api } from '@/api/client'
import type { BathsCompanyListItemDto, CompanyKindsSummaryDto } from '../types'
import type { BathsCompanyCreateInput, BathsCompanyCreatedDto, BathsSlugCheckDto, TrialOutcomeDto, TrialStateDto } from '../cabinet/types'

/** Cabinet: the list, the creation and the trial of the «Бани» line (API_CONTRACT_CYCLE42.md §42.27). */
export const bathsCompaniesApi = {
  kindsSummary: () => api.get<CompanyKindsSummaryDto>('/companies/kinds-summary').then((r) => r.data),
  my: () => api.get<BathsCompanyListItemDto[]>('/baths/companies/my').then((r) => r.data),
  /** 201 with a fresh token (claim of the accepted owner agreement) that MUST replace the stored one. */
  create: (input: BathsCompanyCreateInput) => api.post<BathsCompanyCreatedDto>('/baths/companies', input).then((r) => r.data),
  /** Always 200; a taken or reserved address comes inside the body (`conflict`). */
  slugCheck: (params: { name?: string; slug?: string; companyId?: string }) =>
    api.get<BathsSlugCheckDto>('/baths/slug-check', { params }).then((r) => r.data),
  trialState: () => api.get<TrialStateDto>('/baths/trial').then((r) => r.data),
  /** A refusal is a 409 with the same body shape (`granted: false`) — callers read it from the error. */
  activateTrial: (termsVersion: string) => api.post<TrialOutcomeDto>('/baths/trial', { termsVersion }).then((r) => r.data),
}
