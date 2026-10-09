import { api } from '@/api/client'
import type { BathsCompanyListItemDto, CompanyKindsSummaryDto } from '../types'

/** Cabinet: the «Бани» company (API_CONTRACT_CYCLE42.md). FE-42-5 extends this file with the rest of the cabinet routes. */
export const bathsCompaniesApi = {
  kindsSummary: () => api.get<CompanyKindsSummaryDto>('/companies/kinds-summary').then((r) => r.data),
  my: () => api.get<BathsCompanyListItemDto[]>('/baths/companies/my').then((r) => r.data),
}
